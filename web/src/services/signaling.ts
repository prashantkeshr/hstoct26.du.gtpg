import type { ServerMessage } from '../types'
import { getEndpoint, getAccessToken } from './auth'

type MessageHandler = (msg: ServerMessage) => void
type StateHandler = (state: 'connecting' | 'connected' | 'disconnected' | 'error') => void

export class SignalingClient {
  private ws: WebSocket | null = null
  private onMessage: MessageHandler
  private onStateChange: StateHandler
  private pingInterval: number | null = null
  private reconnectTimeout: number | null = null
  private reconnectAttempts = 0
  private maxReconnectAttempts = 8
  private closed = false

  constructor(onMessage: MessageHandler, onStateChange: StateHandler) {
    this.onMessage = onMessage
    this.onStateChange = onStateChange
  }

  connect(): void {
    const endpoint = getEndpoint()
    const token = getAccessToken()
    if (!endpoint || !token) {
      this.onStateChange('error')
      return
    }

    // Convert http(s):// to ws(s)://
    const wsUrl = endpoint.replace(/^http/, 'ws') + '/ws'
    this.onStateChange('connecting')

    try {
      this.ws = new WebSocket(wsUrl)
    } catch {
      this.scheduleReconnect()
      return
    }

    this.ws.onopen = () => {
      // Send auth as the first message (JWT in message body, not URL)
      this.send({ type: 'Auth', token })
    }

    this.ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data as string) as ServerMessage
        if (msg.type === 'AuthOk') {
          this.reconnectAttempts = 0
          this.onStateChange('connected')
          this.startPing()
        } else if (msg.type === 'AuthFail') {
          this.closed = true
          this.onStateChange('error')
          this.ws?.close()
          return
        }
        this.onMessage(msg)
      } catch { /* ignore parse errors */ }
    }

    this.ws.onclose = () => {
      this.stopPing()
      if (!this.closed) {
        this.onStateChange('disconnected')
        this.scheduleReconnect()
      }
    }

    this.ws.onerror = () => {
      // onclose fires after onerror — reconnect logic handled there
    }
  }

  send(msg: Record<string, unknown>): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg))
    }
  }

  disconnect(): void {
    this.closed = true
    this.clearReconnect()
    this.stopPing()
    if (this.ws) {
      this.send({ type: 'Disconnect' })
      this.ws.close(1000)
      this.ws = null
    }
  }

  private startPing(): void {
    this.pingInterval = window.setInterval(() => {
      this.send({ type: 'Ping' })
    }, 20_000)
  }

  private stopPing(): void {
    if (this.pingInterval !== null) {
      clearInterval(this.pingInterval)
      this.pingInterval = null
    }
  }

  private scheduleReconnect(): void {
    if (this.closed) return
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      this.onStateChange('error')
      return
    }

    const delay = Math.min(1000 * 2 ** this.reconnectAttempts, 30_000)
    this.reconnectAttempts++
    this.reconnectTimeout = window.setTimeout(() => this.connect(), delay)
  }

  private clearReconnect(): void {
    if (this.reconnectTimeout !== null) {
      clearTimeout(this.reconnectTimeout)
      this.reconnectTimeout = null
    }
  }
}
