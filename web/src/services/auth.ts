import type { AuthTokens, DeviceInfo, AccessKey } from '../types'

const ENDPOINT_KEY = 'dh_endpoint'
const ACCESS_TOKEN_KEY = 'dh_at'
const REFRESH_TOKEN_KEY = 'dh_rt'
const SESSION_ID_KEY = 'dh_sid'

// ── Endpoint ──────────────────────────────────────────────────────────────────

export function getEndpoint(): string | null {
  return sessionStorage.getItem(ENDPOINT_KEY)
}

export function setEndpoint(url: string): void {
  sessionStorage.setItem(ENDPOINT_KEY, url.replace(/\/$/, ''))
}

function ep(path: string): string {
  return `${getEndpoint() ?? ''}${path}`
}

// ── Token management ──────────────────────────────────────────────────────────

export function storeTokens(tokens: AuthTokens): void {
  sessionStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken)
  sessionStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken)
  sessionStorage.setItem(SESSION_ID_KEY, tokens.sessionId)
}

export function getAccessToken(): string | null {
  return sessionStorage.getItem(ACCESS_TOKEN_KEY)
}

export function clearTokens(): void {
  sessionStorage.removeItem(ACCESS_TOKEN_KEY)
  sessionStorage.removeItem(REFRESH_TOKEN_KEY)
  sessionStorage.removeItem(SESSION_ID_KEY)
}

export function isAuthenticated(): boolean {
  return getAccessToken() !== null
}

// ── Authenticated fetch helper ────────────────────────────────────────────────

export function authFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const token = getAccessToken()
  return fetch(url, {
    ...init,
    headers: {
      ...(init.headers ?? {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  })
}

// ── Auth calls — return { ok, error? } so callers don't need try/catch ────────

interface CheckResult {
  ok: boolean
  error?: string
  hasOwner?: boolean
}

export async function checkEndpoint(endpointUrl: string): Promise<CheckResult> {
  try {
    const res = await fetch(`${endpointUrl.replace(/\/$/, '')}/api/device/info`, {
      signal: AbortSignal.timeout(8000),
    })
    if (!res.ok) return { ok: false, error: 'Host agent returned an error.' }
    const data = await res.json()
    return { ok: true, hasOwner: data.hasOwner ?? false }
  } catch {
    return { ok: false, error: 'Could not reach host agent. Is it running?' }
  }
}

interface AuthResult {
  ok: boolean
  error?: string
}

export async function login(username: string, password: string): Promise<AuthResult> {
  try {
    const res = await fetch(ep('/api/auth/login'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
      signal: AbortSignal.timeout(10_000),
    })
    if (res.status === 429) return { ok: false, error: 'Too many attempts. Wait 15 minutes.' }
    if (res.status === 401) return { ok: false, error: 'Invalid username or password.' }
    if (!res.ok) return { ok: false, error: 'Login failed. Check your connection.' }
    const tokens: AuthTokens = await res.json()
    storeTokens(tokens)
    return { ok: true }
  } catch {
    return { ok: false, error: 'Connection failed.' }
  }
}

export async function loginWithKey(key: string): Promise<AuthResult> {
  try {
    const res = await fetch(ep('/api/auth/login-key'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ key }),
      signal: AbortSignal.timeout(10_000),
    })
    if (res.status === 429) return { ok: false, error: 'Too many attempts. Wait 15 minutes.' }
    if (res.status === 401) return { ok: false, error: 'Invalid access key.' }
    if (!res.ok) return { ok: false, error: 'Login failed. Check your connection.' }
    const tokens: AuthTokens = await res.json()
    storeTokens(tokens)
    return { ok: true }
  } catch {
    return { ok: false, error: 'Connection failed.' }
  }
}

export async function setupOwner(username: string, password: string): Promise<AuthResult> {
  try {
    const res = await fetch(ep('/api/auth/setup'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
      signal: AbortSignal.timeout(10_000),
    })
    if (res.status === 409) return { ok: false, error: 'Owner already configured.' }
    if (!res.ok) {
      const body = await res.json().catch(() => ({})) as { error?: string }
      return { ok: false, error: body.error ?? 'Setup failed.' }
    }
    const tokens: AuthTokens = await res.json()
    storeTokens(tokens)
    return { ok: true }
  } catch {
    return { ok: false, error: 'Connection failed.' }
  }
}

export async function refreshTokens(): Promise<boolean> {
  const accessToken = getAccessToken()
  const refreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY)
  if (!refreshToken) return false
  try {
    const res = await fetch(ep('/api/auth/refresh'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ accessToken, refreshToken }),
      signal: AbortSignal.timeout(10_000),
    })
    if (!res.ok) { clearTokens(); return false }
    const tokens: AuthTokens = await res.json()
    storeTokens(tokens)
    return true
  } catch {
    return false
  }
}

export async function logout(): Promise<void> {
  const sessionId = sessionStorage.getItem(SESSION_ID_KEY)
  const token = getAccessToken()
  if (token) {
    await fetch(ep('/api/auth/logout'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ sessionId }),
    }).catch(() => {})
  }
  clearTokens()
}

// ── Device API ────────────────────────────────────────────────────────────────

export async function getDevice(): Promise<DeviceInfo> {
  const res = await authFetch(ep('/api/device'), { signal: AbortSignal.timeout(8000) } as RequestInit)
  if (!res.ok) throw new Error('Failed to fetch device info')
  return res.json() as Promise<DeviceInfo>
}

// ── Access key API ────────────────────────────────────────────────────────────

export async function listAccessKeys(): Promise<AccessKey[]> {
  const res = await authFetch(ep('/api/auth/keys'))
  if (!res.ok) throw new Error('Failed to list keys')
  return res.json() as Promise<AccessKey[]>
}

export async function createAccessKey(label: string): Promise<{ id: string; key: string; label: string }> {
  const res = await authFetch(ep('/api/auth/keys'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ label }),
  })
  if (!res.ok) throw new Error('Failed to create key')
  return res.json()
}

export async function revokeAccessKey(id: string): Promise<void> {
  await authFetch(ep(`/api/auth/keys/${id}`), { method: 'DELETE' })
}
