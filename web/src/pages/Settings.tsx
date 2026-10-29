import { useEffect, useState } from 'react'
import Layout from '../components/Layout'
import { getEndpoint, authFetch } from '../services/auth'
import type { AccessKey } from '../types'

export default function Settings() {
  const [keys, setKeys] = useState<AccessKey[]>([])
  const [keysLoading, setKeysLoading] = useState(true)
  const [genLoading, setGenLoading] = useState(false)
  const [newKey, setNewKey] = useState<string | null>(null)
  const [keyLabel, setKeyLabel] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [unattended, setUnattended] = useState(false)
  const [unattendedLoading, setUnattendedLoading] = useState(false)

  const ep = getEndpoint()

  useEffect(() => {
    if (!ep) return
    authFetch(`${ep}/api/auth/keys`)
      .then((r) => r.json())
      .then((data) => { setKeys(data.keys ?? []); setKeysLoading(false) })
      .catch(() => setKeysLoading(false))

    authFetch(`${ep}/api/device`)
      .then((r) => r.json())
      .then((d) => setUnattended(d.unattendedAccess ?? false))
      .catch(() => {})
  }, [ep])

  const generateKey = async () => {
    if (!ep) return
    setGenLoading(true)
    setError('')
    setNewKey(null)
    try {
      const res = await authFetch(`${ep}/api/auth/keys`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ label: keyLabel.trim() || 'Unnamed key' }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.error ?? 'Failed to generate key.'); return }
      setNewKey(data.key)
      setKeyLabel('')
      const listRes = await authFetch(`${ep}/api/auth/keys`)
      const listData = await listRes.json()
      setKeys(listData.keys ?? [])
    } catch {
      setError('Failed to generate key.')
    } finally {
      setGenLoading(false)
    }
  }

  const revokeKey = async (id: string) => {
    if (!ep) return
    try {
      await authFetch(`${ep}/api/auth/keys/${id}`, { method: 'DELETE' })
      setKeys((prev) => prev.filter((k) => k.id !== id))
    } catch {
      setError('Failed to revoke key.')
    }
  }

  const toggleUnattended = async () => {
    if (!ep) return
    setUnattendedLoading(true)
    try {
      const res = await authFetch(`${ep}/api/device/unattended`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled: !unattended }),
      })
      if (res.ok) {
        setUnattended(!unattended)
        setSuccess('Saved.')
        setTimeout(() => setSuccess(''), 2000)
      }
    } catch {
      setError('Failed to update setting.')
    } finally {
      setUnattendedLoading(false)
    }
  }

  return (
    <Layout title="Settings">
      <div className="space-y-6">
        {error && (
          <div className="text-sm text-red-400 bg-red-500/10 border border-red-500/20 rounded-lg px-4 py-3">
            {error}
          </div>
        )}
        {success && (
          <div className="text-sm text-green-400 bg-green-500/10 border border-green-500/20 rounded-lg px-4 py-3">
            {success}
          </div>
        )}

        {/* Unattended access */}
        <div className="card">
          <h2 className="text-sm font-semibold text-white mb-1">Unattended access</h2>
          <p className="text-sm text-zinc-400 mb-4">
            Allow connections without anyone at the host machine.
          </p>
          <div className="flex items-center gap-3">
            <button
              role="switch"
              aria-checked={unattended}
              onClick={toggleUnattended}
              disabled={unattendedLoading}
              className={`relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:ring-offset-surface-0 ${
                unattended ? 'bg-blue-600' : 'bg-surface-3'
              }`}
            >
              <span
                className={`pointer-events-none inline-block h-5 w-5 rounded-full bg-white shadow transition duration-200 ease-in-out ${
                  unattended ? 'translate-x-5' : 'translate-x-0'
                }`}
              />
            </button>
            <span className="text-sm text-zinc-300">{unattended ? 'Enabled' : 'Disabled'}</span>
          </div>
        </div>

        {/* Access keys */}
        <div className="card">
          <h2 className="text-sm font-semibold text-white mb-1">Access keys</h2>
          <p className="text-sm text-zinc-400 mb-4">
            Long-lived keys for connecting without a password. Revoke anytime.
          </p>

          {/* New key revealed */}
          {newKey && (
            <div className="mb-4 bg-green-500/10 border border-green-500/20 rounded-lg px-4 py-3">
              <p className="text-xs text-green-400 font-medium mb-1.5">
                Copy this key — it won't be shown again
              </p>
              <div className="flex items-center gap-2">
                <code className="text-xs font-mono text-green-300 flex-1 break-all">{newKey}</code>
                <button
                  onClick={() => { navigator.clipboard.writeText(newKey); setSuccess('Copied!'); setTimeout(() => setSuccess(''), 1500) }}
                  className="btn-secondary text-xs flex-shrink-0"
                >
                  Copy
                </button>
              </div>
            </div>
          )}

          {/* Generate form */}
          <div className="flex gap-2 mb-4">
            <input
              type="text"
              className="input flex-1"
              placeholder="Label (optional)"
              value={keyLabel}
              onChange={(e) => setKeyLabel(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && generateKey()}
            />
            <button
              onClick={generateKey}
              disabled={genLoading}
              className="btn-primary text-sm flex-shrink-0"
            >
              {genLoading ? 'Generating…' : 'Generate key'}
            </button>
          </div>

          {/* Key list */}
          {keysLoading ? (
            <div className="text-sm text-zinc-500">Loading…</div>
          ) : keys.length === 0 ? (
            <div className="text-sm text-zinc-500">No access keys yet.</div>
          ) : (
            <div className="divide-y divide-border">
              {keys.map((k) => (
                <div key={k.id} className="flex items-center gap-3 py-3 first:pt-0 last:pb-0">
                  <div className="flex-1 min-w-0">
                    <div className="text-sm text-zinc-200 truncate">{k.label || 'Unnamed key'}</div>
                    <div className="text-xs text-zinc-500">
                      Created {new Date(k.createdAt).toLocaleDateString()}
                      {k.lastUsedAt && ` · Last used ${new Date(k.lastUsedAt).toLocaleDateString()}`}
                    </div>
                  </div>
                  <button
                    onClick={() => revokeKey(k.id)}
                    className="text-xs text-red-400 hover:text-red-300 transition-colors flex-shrink-0"
                  >
                    Revoke
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Connection info */}
        <div className="card">
          <h2 className="text-sm font-semibold text-white mb-3">Connection</h2>
          <div className="text-sm space-y-1.5">
            <div className="flex justify-between">
              <span className="text-zinc-500">Host endpoint</span>
              <code className="text-xs font-mono text-zinc-300">{ep ?? '—'}</code>
            </div>
          </div>
        </div>
      </div>
    </Layout>
  )
}
