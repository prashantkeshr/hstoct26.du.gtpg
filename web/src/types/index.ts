// ── Domain types ──────────────────────────────────────────────────────────────

export interface AuthTokens {
  accessToken: string
  refreshToken: string
  expiresIn: number
  sessionId: string
}

export interface DeviceInfo {
  id: string
  name: string
  os: string
  osVersion: string
  agentVersion: string
  screenWidth: number
  screenHeight: number
  lastSeenAt: string
  unattendedAccess: boolean
  status: DeviceStatus
}

export type DeviceStatus =
  | 'online'
  | 'offline'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'auth_failed'

export type ConnectionQuality = 'excellent' | 'good' | 'fair' | 'poor' | 'unknown'

export interface AccessKey {
  id: string
  label: string
  createdAt: string
  expiresAt: string | null
  lastUsedAt: string | null
}

// ── WebSocket message types ───────────────────────────────────────────────────

// Const object usable as enum values at runtime
export const ServerMessageType = {
  AuthOk: 'AuthOk',
  AuthFail: 'AuthFail',
  ScreenInfo: 'ScreenInfo',
  Frame: 'Frame',
  WebRtcOffer: 'WebRtcOffer',
  WebRtcAnswer: 'WebRtcAnswer',
  IceCandidate: 'IceCandidate',
  ClipboardText: 'ClipboardText',
  Pong: 'Pong',
  Error: 'Error',
} as const

export type ClientMessageType =
  | 'Auth'
  | 'MouseMove' | 'MouseButton' | 'MouseScroll'
  | 'KeyDown' | 'KeyUp'
  | 'ClipboardGet' | 'ClipboardSet'
  | 'WebRtcOffer' | 'WebRtcAnswer' | 'IceCandidate'
  | 'Ping' | 'Disconnect'

export interface ServerMessage {
  type: string
  // AuthOk
  sessionId?: string
  screenWidth?: number
  screenHeight?: number
  // AuthFail
  reason?: string
  // Error
  code?: string
  message?: string
  // Frame
  data?: string
  // IceCandidate
  candidate?: string
  sdpMid?: string
  sdpMLineIndex?: number
  // WebRtcOffer/Answer
  sdp?: string
  iceServers?: Array<{ urls: string; username?: string; credential?: string }>
  // ClipboardText
  text?: string
  // ScreenInfo
  width?: number
  height?: number
}

// ── API response types ────────────────────────────────────────────────────────

export interface ApiError {
  error: string
}

export interface DeviceInfoPublic {
  hasOwner: boolean
  agentVersion: string | null
}

export interface ApiResult<T = void> {
  ok: boolean
  error?: string
  data?: T
}
