import { Link } from 'react-router-dom'

export default function Landing() {
  return (
    <div className="min-h-screen flex flex-col">
      {/* Nav */}
      <nav className="border-b border-border">
        <div className="max-w-5xl mx-auto px-4 h-14 flex items-center justify-between">
          <span className="text-sm font-semibold">Dhurta Host</span>
          <div className="flex items-center gap-3">
            <Link to="/help" className="text-sm text-zinc-400 hover:text-zinc-200 transition-colors">
              Help
            </Link>
            <Link to="/auth" className="btn-primary text-sm">
              Connect
            </Link>
          </div>
        </div>
      </nav>

      {/* Hero */}
      <main className="flex-1 max-w-5xl mx-auto px-4">
        <div className="py-20 text-center">
          <div className="inline-flex items-center gap-2 text-xs font-medium text-blue-400 bg-blue-500/10 border border-blue-500/20 rounded-full px-3 py-1 mb-6">
            <span className="w-1.5 h-1.5 rounded-full bg-blue-400 animate-pulse" />
            Self-hosted — no paid services
          </div>

          <h1 className="text-4xl sm:text-5xl font-semibold text-white leading-tight mb-4">
            Your Windows PC,<br />from any browser
          </h1>
          <p className="text-lg text-zinc-400 max-w-xl mx-auto mb-10">
            Secure browser-based remote desktop. See your actual Windows screen, control the real
            mouse and keyboard, run your applications — from anywhere.
          </p>

          <div className="flex items-center justify-center gap-3">
            <Link to="/auth" className="btn-primary px-6 py-2.5 text-sm">
              Open session
            </Link>
            <Link to="/help" className="btn-secondary px-6 py-2.5 text-sm">
              Install Host Agent
            </Link>
          </div>
        </div>

        {/* Feature cards */}
        <div className="grid sm:grid-cols-3 gap-4 pb-16">
          {[
            {
              title: 'Real desktop',
              desc: 'Streams your actual Windows screen. Not a simulation.',
            },
            {
              title: 'End-to-end encrypted',
              desc: 'Screen data travels WebRTC P2P — never through any server.',
            },
            {
              title: 'Zero infrastructure cost',
              desc: 'Runs on your home PC. Only requires a free Cloudflare account.',
            },
          ].map((f) => (
            <div key={f.title} className="card">
              <h3 className="text-sm font-semibold text-white mb-1.5">{f.title}</h3>
              <p className="text-sm text-zinc-400 leading-relaxed">{f.desc}</p>
            </div>
          ))}
        </div>

        {/* Security notice */}
        <div className="card border-amber-500/20 bg-amber-500/5 mb-16">
          <h3 className="text-sm font-semibold text-amber-300 mb-2">Security model</h3>
          <ul className="text-sm text-zinc-400 space-y-1">
            <li>— Authentication required before any WebSocket connection is established</li>
            <li>— Screen stream goes WebRTC P2P: Cloudflare never sees your screen</li>
            <li>— Argon2id password hashing, short-lived JWT tokens, per-key revocation</li>
            <li>— Host Agent listens on localhost only — no exposed ports on your router</li>
            <li>— Rate-limited: 5 failed auth attempts triggers a 15-minute lockout</li>
          </ul>
        </div>
      </main>
    </div>
  )
}
