import { create } from 'zustand'
import type { DeviceInfo, ConnectionQuality } from '../types'

// ── Auth store ────────────────────────────────────────────────────────────────

interface AuthState {
  isAuthenticated: boolean
  isLoading: boolean
  endpoint: string
  setAuthenticated: (v: boolean) => void
  setLoading: (v: boolean) => void
  setEndpoint: (v: string) => void
}

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  isLoading: false,
  endpoint: '',
  setAuthenticated: (v) => set({ isAuthenticated: v }),
  setLoading: (v) => set({ isLoading: v }),
  setEndpoint: (v) => set({ endpoint: v }),
}))

// ── Device store ──────────────────────────────────────────────────────────────

interface DeviceState {
  device: DeviceInfo | null
  setDevice: (d: DeviceInfo | null) => void
}

export const useDeviceStore = create<DeviceState>((set) => ({
  device: null,
  setDevice: (d) => set({ device: d }),
}))

// ── Connection store ──────────────────────────────────────────────────────────

type ConnectionStatus =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'error'

interface ConnectionState {
  status: ConnectionStatus
  quality: ConnectionQuality
  screenWidth: number
  screenHeight: number
  sessionId: string | null
  setStatus: (s: ConnectionStatus) => void
  setQuality: (q: ConnectionQuality) => void
  setScreenDimensions: (w: number, h: number) => void
  setSessionId: (id: string | null) => void
  reset: () => void
}

export const useConnectionStore = create<ConnectionState>((set) => ({
  status: 'idle',
  quality: 'unknown',
  screenWidth: 1920,
  screenHeight: 1080,
  sessionId: null,
  setStatus: (s) => set({ status: s }),
  setQuality: (q) => set({ quality: q }),
  setScreenDimensions: (w, h) => set({ screenWidth: w, screenHeight: h }),
  setSessionId: (id) => set({ sessionId: id }),
  reset: () => set({ status: 'idle', quality: 'unknown', sessionId: null }),
}))
