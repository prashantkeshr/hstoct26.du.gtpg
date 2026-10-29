# Dhurta Host

Browser-based Windows remote desktop. Self-hosted, zero infrastructure cost, no paid services.

## How it works

```
Browser  ──WebRTC P2P──▶  Windows PC
   │                           │
   └──── Cloudflare Tunnel ────┘
          (signaling only)
```

- **Host Agent** (C# .NET 8) runs on your Windows PC, captures the screen, injects input
- **Kestrel** listens on `127.0.0.1:8765` only — never exposed directly to the internet  
- **Cloudflare Tunnel** (`cloudflared`) creates an outbound tunnel to expose the signaling WebSocket  
- **WebRTC P2P** carries the actual screen stream — Cloudflare never sees your screen content  
- **Web frontend** (React + Vite + TypeScript) is hosted on GitHub Pages

## Stack

| Layer | Technology |
|---|---|
| Host Agent | C# .NET 8, ASP.NET Core Kestrel, WinForms tray |
| Screen capture | Windows.Graphics.Capture API, DXGI fallback *(Phase 2)* |
| Input injection | Win32 `SendInput` *(Phase 2)* |
| Tunnel | Cloudflare Tunnel (free) |
| STUN | `stun.l.google.com`, `stun.cloudflare.com` (free, no account) |
| TURN relay | Self-hosted coturn *(Phase 2)* |
| Frontend | React 19, Vite 6, TypeScript, Tailwind CSS, Zustand |
| Auth | Argon2id, JWT HS256, access keys (base32) |
| Storage | SQLite (WAL mode) |

## Security

- Auth required before WebSocket opens; JWT validated within 8 s or connection closed
- Argon2id password hashing (64 MB, 3 iterations)
- Rate-limited: 5 failed attempts → 15-min IP lockout
- Typed WebSocket message protocol — no shell execution possible
- CORS restricted to `prashantkeshr.github.io` + `localhost:5173`
- Session tokens stored in `sessionStorage` (cleared on tab close)

See [`docs/security.md`](docs/security.md) for full details.

## Phase status

| Phase | Description | Status |
|---|---|---|
| 1 | Auth, signaling, access keys, tray | ✅ Complete |
| 2 | Screen streaming (WGC + JPEG frames) | Planned |
| 3 | WebRTC P2P upgrade | Planned |
| 4 | Input forwarding | Planned |
| 5 | TURN relay + reconnect | Planned |
| 6 | Installer (Inno Setup) | Planned |

## Quick start

### Host Agent

```powershell
cd host-agent/src/DhurtaHost
dotnet run
```

The agent starts Kestrel on port 8765, launches a Cloudflare Quick Tunnel, and shows the URL in a tray balloon tip.

### Web frontend (dev)

```bash
cd web
npm install
npm run dev        # http://localhost:5173
```

## Repository layout

```
dhurta-host/
├── host-agent/src/DhurtaHost/   # C# .NET 8 host agent
├── web/                         # React + Vite frontend
├── infrastructure/
│   ├── cloudflared/             # Named tunnel config template
│   └── coturn/                  # TURN relay config template
├── docs/
│   ├── security.md
│   ├── setup-windows.md
│   └── cloudflare-setup.md
└── .github/workflows/
    └── deploy-web.yml           # GitHub Pages deploy
```

## License

MIT
