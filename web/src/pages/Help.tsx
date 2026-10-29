import { Link } from 'react-router-dom'

export default function Help() {
  return (
    <div className="min-h-screen">
      {/* Nav */}
      <nav className="border-b border-border">
        <div className="max-w-5xl mx-auto px-4 h-14 flex items-center justify-between">
          <Link to="/" className="text-sm font-semibold text-white">Dhurta Host</Link>
          <Link to="/auth" className="btn-primary text-sm">Connect</Link>
        </div>
      </nav>

      <div className="max-w-3xl mx-auto px-4 py-12">
        <h1 className="text-2xl font-semibold text-white mb-2">Setup guide</h1>
        <p className="text-zinc-400 mb-10">
          Get your Windows PC ready for remote access in about 10 minutes.
        </p>

        <div className="space-y-8">
          <Section step={1} title="Install .NET 8 Runtime">
            <p>Download and install the .NET 8 Desktop Runtime from Microsoft:</p>
            <Code>winget install Microsoft.DotNet.DesktopRuntime.8</Code>
            <p>Or download the installer from <em>dotnet.microsoft.com/download/dotnet/8.0</em>.</p>
          </Section>

          <Section step={2} title="Install the Host Agent">
            <p>Download the latest <strong>DhurtaHost.exe</strong> from the Releases page on GitHub.</p>
            <p>Run it — a tray icon appears in the system tray (bottom-right of the taskbar). On first launch, the agent starts Kestrel on <code className="text-xs font-mono bg-surface-2 px-1.5 py-0.5 rounded">127.0.0.1:8765</code>.</p>
          </Section>

          <Section step={3} title="Install cloudflared">
            <p>The host agent uses Cloudflare Tunnel to expose the signaling server without opening any ports on your router.</p>
            <Code>winget install Cloudflare.cloudflared</Code>
            <p>The Host Agent automatically starts a quick tunnel on launch. The tunnel URL appears in a balloon tip and is accessible via <em>right-click → Copy tunnel URL</em> on the tray icon.</p>
            <div className="card border-amber-500/20 bg-amber-500/5 mt-3">
              <p className="text-xs text-amber-300">
                <strong>Named tunnel (optional, recommended)</strong> — a quick tunnel URL changes every restart.
                To get a stable URL, create a named Cloudflare Tunnel and place
                <code className="mx-1 font-mono bg-surface-2 px-1 py-0.5 rounded">config.yml</code>
                at <code className="font-mono bg-surface-2 px-1 py-0.5 rounded">%APPDATA%\DhurtaHost\tunnel\config.yml</code>.
                See the <em>cloudflare-setup.md</em> doc in the repository for full instructions.
              </p>
            </div>
          </Section>

          <Section step={4} title="First-time setup in the browser">
            <p>Open this web app, click <strong>Connect</strong>, and paste your tunnel URL.</p>
            <p>Because it's your first connection, you'll be prompted to create an owner account. Choose a strong password (12+ characters).</p>
          </Section>

          <Section step={5} title="Access keys (optional)">
            <p>For quick access from other devices without entering a password:</p>
            <ol className="list-decimal list-inside space-y-1 text-zinc-400">
              <li>Sign in with your password</li>
              <li>Go to <strong>Settings → Access Keys</strong></li>
              <li>Click <strong>Generate key</strong> — copy it immediately, it won't be shown again</li>
              <li>On the next device, paste the key on the auth screen</li>
            </ol>
          </Section>

          <Section step={6} title="Autostart on login (optional)">
            <p>To have the Host Agent start automatically with Windows:</p>
            <Code>
              {`# Run in PowerShell
$target = "$env:APPDATA\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\DhurtaHost.lnk"
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($target)
$sc.TargetPath = "C:\\path\\to\\DhurtaHost.exe"
$sc.Save()`}
            </Code>
            <p>Replace the path with wherever you saved DhurtaHost.exe.</p>
          </Section>
        </div>

        {/* Security section */}
        <div className="mt-12">
          <h2 className="text-lg font-semibold text-white mb-4">Security model</h2>
          <div className="card space-y-3 text-sm text-zinc-400">
            <p>
              <strong className="text-zinc-200">Signaling only through Cloudflare.</strong>{' '}
              Only small JSON messages (WebSocket handshake + WebRTC signaling) pass through Cloudflare Tunnel.
              Your screen content travels WebRTC P2P directly from your PC to your browser — Cloudflare never sees it.
            </p>
            <p>
              <strong className="text-zinc-200">Auth-first WebSocket.</strong>{' '}
              Every WebSocket connection must present a valid JWT within 8 seconds or it is closed.
              The JWT is a 15-minute access token. Refresh tokens live 7 days and are stored as SHA-256 hashes.
            </p>
            <p>
              <strong className="text-zinc-200">Argon2id password hashing.</strong>{' '}
              64 MB memory cost, 3 iterations, 16-byte random salt. Timing-safe comparison.
            </p>
            <p>
              <strong className="text-zinc-200">Rate limiting.</strong>{' '}
              5 failed authentication attempts → 15-minute IP lockout. Respects{' '}
              <code className="text-xs font-mono bg-surface-2 px-1 py-0.5 rounded">CF-Connecting-IP</code>{' '}
              so the real client IP is locked, not Cloudflare's.
            </p>
            <p>
              <strong className="text-zinc-200">No ports exposed.</strong>{' '}
              Kestrel listens on <code className="text-xs font-mono bg-surface-2 px-1 py-0.5 rounded">127.0.0.1:8765</code> only.
              All public access goes through Cloudflare Tunnel's outbound connection from your PC — your router firewall is never touched.
            </p>
            <p>
              <strong className="text-zinc-200">Typed message protocol.</strong>{' '}
              The WebSocket only accepts a fixed set of typed message enums (MouseMove, KeyDown, etc.).
              No shell execution or arbitrary command injection is possible.
            </p>
          </div>
        </div>

        {/* FAQ */}
        <div className="mt-12">
          <h2 className="text-lg font-semibold text-white mb-4">FAQ</h2>
          <div className="space-y-4">
            <Faq q="The tunnel URL changes every time I restart — how do I get a stable URL?">
              Create a named Cloudflare Tunnel. Follow the cloudflare-setup.md guide in the repository.
              A named tunnel gives you a permanent hostname under your Cloudflare account (free plan works).
            </Faq>
            <Faq q="Screen streaming isn't working yet — is the app usable?">
              Phase 1 (auth, signaling, access keys) is fully functional. Screen streaming and input forwarding
              are Phase 2/3 features, coming in the next release.
            </Faq>
            <Faq q="Can someone intercept my session via Cloudflare?">
              Cloudflare sees only the signaling WebSocket messages (SDP offer/answer, ICE candidates — all JSON).
              Your screen content flows WebRTC P2P and is encrypted end-to-end by DTLS-SRTP.
              Cloudflare cannot decrypt it.
            </Faq>
            <Faq q="What happens if the tunnel goes down mid-session?">
              The browser's signaling WebSocket will disconnect. The WebSocket client retries with exponential
              backoff (up to 8 attempts). If the WebRTC connection is already established, it may stay alive
              through ICE keep-alives even without the signaling path.
            </Faq>
          </div>
        </div>
      </div>
    </div>
  )
}

function Section({ step, title, children }: { step: number; title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center gap-3 mb-3">
        <span className="flex-shrink-0 w-6 h-6 rounded-full bg-blue-600 flex items-center justify-center text-xs font-semibold text-white">
          {step}
        </span>
        <h2 className="text-base font-semibold text-white">{title}</h2>
      </div>
      <div className="ml-9 space-y-2 text-sm text-zinc-400 leading-relaxed">
        {children}
      </div>
    </div>
  )
}

function Code({ children }: { children: React.ReactNode }) {
  return (
    <pre className="bg-surface-1 border border-border rounded-lg px-4 py-3 text-xs font-mono text-zinc-200 overflow-x-auto whitespace-pre-wrap">
      {children}
    </pre>
  )
}

function Faq({ q, children }: { q: string; children: React.ReactNode }) {
  return (
    <div className="card">
      <h3 className="text-sm font-semibold text-white mb-1.5">{q}</h3>
      <p className="text-sm text-zinc-400 leading-relaxed">{children}</p>
    </div>
  )
}
