import { useEffect, useRef, useState, useCallback } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { SignalingClient } from '../services/signaling'
import { getEndpoint, getAccessToken } from '../services/auth'
import { useConnectionStore } from '../stores'
import type { ServerMessage } from '../types'
import { ServerMessageType } from '../types'

type ConnectionState = 'connecting' | 'auth' | 'streaming' | 'reconnecting' | 'error' | 'closed'
type IceServer = { urls: string; username?: string; credential?: string }

export default function Workspace() {
  const { deviceId: _deviceId } = useParams<{ deviceId: string }>()
  const navigate = useNavigate()

  const canvasRef = useRef<HTMLCanvasElement>(null)
  const signalingRef = useRef<SignalingClient | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const pcRef = useRef<RTCPeerConnection | null>(null)

  const [state, setState] = useState<ConnectionState>('connecting')
  const [errorMsg, setErrorMsg] = useState('')
  const [showOverlay, setShowOverlay] = useState(true)

  const setStatus = useConnectionStore((s) => s.setStatus)
  const setScreenDimensions = useConnectionStore((s) => s.setScreenDimensions)

  const handleStateChange = useCallback(
    (wsState: 'connecting' | 'connected' | 'disconnected' | 'error') => {
      if (wsState === 'connecting') {
        // Close any stale WebRTC connection from a previous session/reconnect
        if (pcRef.current) { pcRef.current.close(); pcRef.current = null }
        setState((prev) => prev === 'reconnecting' ? 'reconnecting' : 'connecting')
        setStatus('connecting')
        setShowOverlay(true)
      } else if (wsState === 'connected') {
        setState('auth')
      } else if (wsState === 'disconnected') {
        // Auto-reconnect is in flight — show reconnecting rather than "Session ended"
        setState('reconnecting')
        setStatus('connecting')
        setShowOverlay(true)
      } else if (wsState === 'error') {
        setState('error')
        setStatus('error')
        setErrorMsg('Could not reconnect. Check the host agent and your network.')
      }
    },
    [setStatus]
  )

  // Set up a WebRTC peer connection from the host's SDP offer.
  // iceServers: forwarded from the server's offer (includes STUN + optional TURN).
  // Frames arrive as raw JPEG bytes over the DataChannel; WebSocket base64 is the fallback
  // while ICE is negotiating.
  const initWebRtc = useCallback(async (offerSdp: string, iceServers?: IceServer[]) => {
    const servers = iceServers && iceServers.length > 0 ? iceServers : [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun.cloudflare.com:3478' },
    ]
    const pc = new RTCPeerConnection({ iceServers: servers })
    pcRef.current = pc

    // Receive raw JPEG frames over the "screen" DataChannel
    pc.ondatachannel = (event) => {
      const dc = event.channel
      dc.binaryType = 'arraybuffer'
      dc.onmessage = (e) => {
        if (!(e.data instanceof ArrayBuffer)) return
        const canvas = canvasRef.current
        if (!canvas) return
        const ctx = canvas.getContext('2d')
        if (!ctx) return
        createImageBitmap(new Blob([e.data], { type: 'image/jpeg' }))
          .then((bitmap) => { ctx.drawImage(bitmap, 0, 0); bitmap.close() })
          .catch(() => {})
      }
    }

    // Forward our ICE candidates to the host over the WebSocket signaling channel
    pc.onicecandidate = (event) => {
      if (!event.candidate) return
      signalingRef.current?.send({
        type: 'IceCandidate',
        candidate: event.candidate.candidate,
        sdpMid: event.candidate.sdpMid ?? undefined,
        sdpMLineIndex: event.candidate.sdpMLineIndex ?? undefined,
      })
    }

    await pc.setRemoteDescription({ type: 'offer', sdp: offerSdp })
    const answer = await pc.createAnswer()
    await pc.setLocalDescription(answer)

    if (answer.sdp) {
      signalingRef.current?.send({ type: 'WebRtcAnswer', sdp: answer.sdp })
    }
  }, [])

  const handleMessage = useCallback((msg: ServerMessage) => {
    switch (msg.type) {
      case ServerMessageType.AuthOk:
        setState('auth')
        break

      case ServerMessageType.ScreenInfo: {
        const w = msg.screenWidth ?? msg.width ?? 1920
        const h = msg.screenHeight ?? msg.height ?? 1080
        setScreenDimensions(w, h)
        if (canvasRef.current) {
          canvasRef.current.width = w
          canvasRef.current.height = h
        }
        setState('streaming')
        setShowOverlay(false)
        break
      }

      // WebSocket JPEG fallback — active while WebRTC is negotiating or TURN is needed
      case ServerMessageType.Frame: {
        const canvas = canvasRef.current
        if (!canvas || !msg.data) break
        const ctx = canvas.getContext('2d')
        if (!ctx) break
        const img = new Image()
        img.onload = () => ctx.drawImage(img, 0, 0)
        img.src = `data:image/jpeg;base64,${msg.data}`
        break
      }

      // WebRTC signaling — iceServers from the offer override the defaults
      case ServerMessageType.WebRtcOffer:
        if (msg.sdp) initWebRtc(msg.sdp, msg.iceServers).catch(console.error)
        break

      case ServerMessageType.IceCandidate:
        if (pcRef.current && msg.candidate) {
          pcRef.current.addIceCandidate({
            candidate: msg.candidate,
            sdpMid: msg.sdpMid ?? undefined,
            sdpMLineIndex: msg.sdpMLineIndex ?? undefined,
          }).catch(() => {})
        }
        break

      case ServerMessageType.AuthFail:
        setState('error')
        setErrorMsg('Authentication failed. Please sign in again.')
        break

      case ServerMessageType.Error:
        setErrorMsg(msg.message ?? 'An error occurred.')
        break
    }
  }, [setScreenDimensions, initWebRtc])

  useEffect(() => {
    const ep = getEndpoint()
    const token = getAccessToken()
    if (!ep || !token) {
      navigate('/auth')
      return
    }

    setStatus('connecting')

    const sc = new SignalingClient(handleMessage, handleStateChange)
    signalingRef.current = sc
    sc.connect()

    return () => {
      sc.disconnect()
      signalingRef.current = null
      pcRef.current?.close()
      pcRef.current = null
    }
  }, [navigate, handleMessage, handleStateChange, setStatus])

  // Forward pointer events
  const sendPointer = useCallback((e: React.PointerEvent<HTMLCanvasElement>) => {
    const sc = signalingRef.current
    if (!sc || state !== 'streaming') return
    const canvas = canvasRef.current!
    const rect = canvas.getBoundingClientRect()
    const scaleX = canvas.width / rect.width
    const scaleY = canvas.height / rect.height
    const x = Math.round((e.clientX - rect.left) * scaleX)
    const y = Math.round((e.clientY - rect.top) * scaleY)
    sc.send({ type: 'MouseMove', x, y })
  }, [state])

  const sendMouseButton = useCallback((e: React.MouseEvent<HTMLCanvasElement>, down: boolean) => {
    const sc = signalingRef.current
    if (!sc || state !== 'streaming') return
    e.preventDefault()
    const buttonName = (['left', 'middle', 'right'] as const)[e.button] ?? 'left'
    sc.send({ type: 'MouseButton', button: buttonName, down })
  }, [state])

  const sendScroll = useCallback((e: React.WheelEvent<HTMLCanvasElement>, ) => {
    const sc = signalingRef.current
    if (!sc || state !== 'streaming') return
    e.preventDefault()
    sc.send({ type: 'MouseScroll', deltaX: e.deltaX, deltaY: e.deltaY })
  }, [state])

  const sendKey = useCallback((e: React.KeyboardEvent, down: boolean) => {
    const sc = signalingRef.current
    if (!sc || state !== 'streaming') return
    e.preventDefault()
    sc.send({ type: down ? 'KeyDown' : 'KeyUp', key: e.key, code: e.code })
  }, [state])

  return (
    <div
      ref={containerRef}
      className="relative w-full h-screen bg-black flex items-center justify-center overflow-hidden"
      tabIndex={-1}
    >
      {/* Canvas */}
      <canvas
        ref={canvasRef}
        className={`max-w-full max-h-full object-contain cursor-none ${state !== 'streaming' ? 'opacity-0' : 'opacity-100'}`}
        onPointerMove={sendPointer}
        onMouseDown={(e) => sendMouseButton(e, true)}
        onMouseUp={(e) => sendMouseButton(e, false)}
        onWheel={sendScroll}
        onContextMenu={(e) => e.preventDefault()}
        onKeyDown={(e) => sendKey(e, true)}
        onKeyUp={(e) => sendKey(e, false)}
        tabIndex={0}
      />

      {/* Overlay */}
      {showOverlay && (
        <div className="absolute inset-0 flex flex-col items-center justify-center bg-black/80 backdrop-blur-sm">
          {(state === 'connecting' || state === 'auth') && (
            <StatusOverlay
              title={state === 'connecting' ? 'Connecting…' : 'Authenticating…'}
              subtitle={state === 'connecting'
                ? 'Establishing secure WebSocket connection'
                : 'Verifying session token'}
            />
          )}
          {state === 'reconnecting' && (
            <StatusOverlay
              title="Reconnecting…"
              subtitle="Network interrupted — restoring connection automatically"
            />
          )}
          {state === 'error' && (
            <div className="text-center">
              <div className="text-red-400 text-5xl mb-4">✕</div>
              <h2 className="text-white text-lg font-semibold mb-2">Connection failed</h2>
              <p className="text-zinc-400 text-sm mb-6 max-w-xs">{errorMsg}</p>
              <div className="flex gap-3 justify-center">
                <button onClick={() => navigate('/dashboard')} className="btn-secondary text-sm">
                  Back to devices
                </button>
                <button onClick={() => window.location.reload()} className="btn-primary text-sm">
                  Retry
                </button>
              </div>
            </div>
          )}
          {state === 'closed' && (
            <div className="text-center">
              <h2 className="text-white text-lg font-semibold mb-2">Session ended</h2>
              <p className="text-zinc-400 text-sm mb-6">The connection was closed.</p>
              <button onClick={() => navigate('/dashboard')} className="btn-secondary text-sm">
                Back to devices
              </button>
            </div>
          )}
        </div>
      )}

      {/* Top bar (streaming mode) */}
      {state === 'streaming' && (
        <div className="absolute top-0 inset-x-0 h-8 bg-black/40 backdrop-blur-sm flex items-center px-4 gap-4 opacity-0 hover:opacity-100 transition-opacity duration-200">
          <span className="text-xs text-zinc-300 font-medium flex-1">Dhurta Host</span>
          <span className="status-dot bg-green-400" />
          <span className="text-xs text-zinc-400">Connected</span>
          <button
            onClick={() => {
              signalingRef.current?.disconnect()
              setState('closed')
              navigate('/dashboard')
            }}
            className="text-xs text-zinc-400 hover:text-red-400 transition-colors ml-2"
          >
            Disconnect
          </button>
        </div>
      )}
    </div>
  )
}

function StatusOverlay({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="text-center">
      <div className="w-8 h-8 rounded-full border-2 border-zinc-700 border-t-blue-500 animate-spin mx-auto mb-5" />
      <h2 className="text-white text-base font-semibold mb-1">{title}</h2>
      <p className="text-zinc-400 text-sm">{subtitle}</p>
    </div>
  )
}
