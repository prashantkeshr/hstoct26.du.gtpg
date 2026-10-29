import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { checkEndpoint, login, loginWithKey, setupOwner, getEndpoint, setEndpoint } from '../services/auth'
import { useAuthStore } from '../stores'

type Step = 'endpoint' | 'setup' | 'login' | 'key'

export default function Auth() {
  const navigate = useNavigate()
  const setAuthenticated = useAuthStore((s) => s.setAuthenticated)
  const setEndpointStore = useAuthStore((s) => s.setEndpoint)

  const [step, setStep] = useState<Step>('endpoint')
  const [endpointInput, setEndpointInput] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [accessKey, setAccessKey] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [hasOwner, setHasOwner] = useState<boolean | null>(null)

  useEffect(() => {
    const saved = getEndpoint()
    if (saved) setEndpointInput(saved)
  }, [])

  const handleEndpointCheck = async () => {
    const url = endpointInput.trim()
    if (!url) return
    setLoading(true)
    setError('')
    try {
      const result = await checkEndpoint(url)
      if (!result.ok) {
        setError(result.error ?? 'Could not reach host agent.')
        return
      }
      setEndpoint(url)
      setEndpointStore(url)
      setHasOwner(result.hasOwner ?? false)
      setStep(result.hasOwner ? 'login' : 'setup')
    } catch {
      setError('Connection failed. Is the Host Agent running?')
    } finally {
      setLoading(false)
    }
  }

  const handleSetup = async () => {
    if (password !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }
    if (password.length < 12) {
      setError('Password must be at least 12 characters.')
      return
    }
    setLoading(true)
    setError('')
    try {
      const result = await setupOwner(username, password)
      if (!result.ok) {
        setError(result.error ?? 'Setup failed.')
        return
      }
      setAuthenticated(true)
      navigate('/dashboard')
    } catch {
      setError('Setup failed. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  const handleLogin = async () => {
    setLoading(true)
    setError('')
    try {
      const result = await login(username, password)
      if (!result.ok) {
        setError(result.error ?? 'Invalid credentials.')
        return
      }
      setAuthenticated(true)
      navigate('/dashboard')
    } catch {
      setError('Login failed. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  const handleKeyLogin = async () => {
    setLoading(true)
    setError('')
    try {
      const result = await loginWithKey(accessKey.trim())
      if (!result.ok) {
        setError(result.error ?? 'Invalid access key.')
        return
      }
      setAuthenticated(true)
      navigate('/dashboard')
    } catch {
      setError('Login failed. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        {/* Logo */}
        <div className="text-center mb-8">
          <div className="text-base font-semibold text-white mb-1">Dhurta Host</div>
          <p className="text-sm text-zinc-400">
            {step === 'endpoint' && 'Enter your Host Agent URL'}
            {step === 'setup' && 'Create your account'}
            {step === 'login' && 'Sign in to your session'}
            {step === 'key' && 'Sign in with access key'}
          </p>
        </div>

        <div className="card">
          {error && (
            <div className="mb-4 text-sm text-red-400 bg-red-500/10 border border-red-500/20 rounded-lg px-3 py-2.5">
              {error}
            </div>
          )}

          {/* Endpoint step */}
          {step === 'endpoint' && (
            <div className="space-y-4">
              <div>
                <label className="label">Host Agent URL</label>
                <input
                  type="url"
                  className="input font-mono"
                  placeholder="https://abc123.trycloudflare.com"
                  value={endpointInput}
                  onChange={(e) => setEndpointInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleEndpointCheck()}
                  autoFocus
                />
                <p className="mt-1.5 text-xs text-zinc-500">
                  The tunnel URL shown in your Host Agent tray icon.
                </p>
              </div>
              <button
                onClick={handleEndpointCheck}
                disabled={loading || !endpointInput.trim()}
                className="btn-primary w-full"
              >
                {loading ? 'Connecting…' : 'Connect'}
              </button>
            </div>
          )}

          {/* Setup step */}
          {step === 'setup' && (
            <div className="space-y-4">
              <div className="text-xs text-zinc-400 bg-surface-2 rounded-lg px-3 py-2.5 border border-border">
                First-run setup — creating owner account on the host.
              </div>
              <div>
                <label className="label">Username</label>
                <input
                  type="text"
                  className="input"
                  placeholder="admin"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  autoFocus
                />
              </div>
              <div>
                <label className="label">Password</label>
                <input
                  type="password"
                  className="input"
                  placeholder="At least 12 characters"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>
              <div>
                <label className="label">Confirm Password</label>
                <input
                  type="password"
                  className="input"
                  placeholder="Repeat password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSetup()}
                />
              </div>
              <button
                onClick={handleSetup}
                disabled={loading || !username || !password || !confirmPassword}
                className="btn-primary w-full"
              >
                {loading ? 'Creating account…' : 'Create account'}
              </button>
            </div>
          )}

          {/* Login step */}
          {step === 'login' && (
            <div className="space-y-4">
              <div>
                <label className="label">Username</label>
                <input
                  type="text"
                  className="input"
                  placeholder="admin"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  autoFocus
                />
              </div>
              <div>
                <label className="label">Password</label>
                <input
                  type="password"
                  className="input"
                  placeholder="Password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleLogin()}
                />
              </div>
              <button
                onClick={handleLogin}
                disabled={loading || !username || !password}
                className="btn-primary w-full"
              >
                {loading ? 'Signing in…' : 'Sign in'}
              </button>
              <button
                onClick={() => { setStep('key'); setError('') }}
                className="btn-secondary w-full text-xs"
              >
                Use access key instead
              </button>
            </div>
          )}

          {/* Key login step */}
          {step === 'key' && (
            <div className="space-y-4">
              <div>
                <label className="label">Access Key</label>
                <input
                  type="password"
                  className="input font-mono"
                  placeholder="dh_…"
                  value={accessKey}
                  onChange={(e) => setAccessKey(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleKeyLogin()}
                  autoFocus
                />
                <p className="mt-1.5 text-xs text-zinc-500">
                  Generated in Settings → Access Keys on the host machine.
                </p>
              </div>
              <button
                onClick={handleKeyLogin}
                disabled={loading || !accessKey.trim()}
                className="btn-primary w-full"
              >
                {loading ? 'Signing in…' : 'Sign in with key'}
              </button>
              {hasOwner && (
                <button
                  onClick={() => { setStep('login'); setError('') }}
                  className="btn-secondary w-full text-xs"
                >
                  Use password instead
                </button>
              )}
            </div>
          )}

          {/* Change endpoint */}
          {step !== 'endpoint' && (
            <button
              onClick={() => { setStep('endpoint'); setError('') }}
              className="mt-4 w-full text-xs text-zinc-500 hover:text-zinc-300 transition-colors py-1"
            >
              ← Change host endpoint
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
