import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import { getEndpoint, authFetch } from '../services/auth'
import { useDeviceStore } from '../stores'
import type { DeviceInfo } from '../types'

export default function Dashboard() {
  const navigate = useNavigate()
  const device = useDeviceStore((s) => s.device)
  const setDevice = useDeviceStore((s) => s.setDevice)
  const [loading, setLoading] = useState(!device)
  const [error, setError] = useState('')

  useEffect(() => {
    if (device) return
    const load = async () => {
      const ep = getEndpoint()
      if (!ep) { setError('No endpoint configured.'); setLoading(false); return }
      try {
        const res = await authFetch(`${ep}/api/device`)
        if (!res.ok) { setError('Failed to load device info.'); return }
        const data: DeviceInfo = await res.json()
        setDevice(data)
      } catch {
        setError('Could not reach host agent.')
      } finally {
        setLoading(false)
      }
    }
    load()
  }, [device, setDevice])

  return (
    <Layout title="Devices">
      {loading && (
        <div className="flex items-center gap-3 text-sm text-zinc-400">
          <span className="w-4 h-4 rounded-full border-2 border-zinc-600 border-t-blue-500 animate-spin" />
          Loading device…
        </div>
      )}

      {error && (
        <div className="text-sm text-red-400 bg-red-500/10 border border-red-500/20 rounded-lg px-4 py-3">
          {error}
        </div>
      )}

      {device && !loading && (
        <div className="space-y-4">
          <DeviceCard device={device} onConnect={() => navigate(`/workspace/${device.id}`)} />

          <div className="card">
            <h2 className="text-sm font-semibold text-white mb-4">About this connection</h2>
            <div className="space-y-2 text-sm text-zinc-400">
              <Row label="Screen" value={`${device.screenWidth} × ${device.screenHeight}`} />
              <Row label="OS" value={device.os} />
              <Row label="Agent version" value={device.agentVersion} />
              <Row label="Endpoint" value={getEndpoint() ?? '—'} mono />
            </div>
          </div>
        </div>
      )}
    </Layout>
  )
}

function DeviceCard({ device, onConnect }: { device: DeviceInfo; onConnect: () => void }) {
  const isOnline = true

  return (
    <div className="card flex items-center gap-4">
      {/* Status dot + icon */}
      <div className="flex-shrink-0 w-10 h-10 rounded-lg bg-surface-2 border border-border flex items-center justify-center">
        <svg className="w-5 h-5 text-zinc-300" viewBox="0 0 20 20" fill="currentColor">
          <rect x="3" y="4" width="14" height="10" rx="1.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
          <path d="M7 16h6M10 14v2" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
        </svg>
      </div>

      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-white truncate">{device.name}</span>
          <span className={`status-dot ${isOnline ? 'bg-green-400' : 'bg-zinc-600'}`} />
        </div>
        <div className="text-xs text-zinc-400 mt-0.5">{device.os}</div>
      </div>

      <button onClick={onConnect} className="btn-primary flex-shrink-0 text-sm">
        Connect
      </button>
    </div>
  )
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-4">
      <span className="text-zinc-500 flex-shrink-0">{label}</span>
      <span className={`text-right break-all ${mono ? 'font-mono text-xs' : ''}`}>{value}</span>
    </div>
  )
}
