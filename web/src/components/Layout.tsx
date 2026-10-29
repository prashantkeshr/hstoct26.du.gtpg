import { Link, useNavigate, useLocation } from 'react-router-dom'
import { logout } from '../services/auth'
import { useAuthStore, useDeviceStore } from '../stores'

interface LayoutProps {
  children: React.ReactNode
  title?: string
}

export default function Layout({ children, title }: LayoutProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const setAuthenticated = useAuthStore((s) => s.setAuthenticated)
  const device = useDeviceStore((s) => s.device)

  const handleLogout = async () => {
    await logout()
    setAuthenticated(false)
    navigate('/auth')
  }

  const navLinks = [
    { to: '/dashboard', label: 'Devices' },
    { to: '/settings', label: 'Settings' },
    { to: '/help', label: 'Help' },
  ]

  return (
    <div className="min-h-screen flex flex-col">
      {/* Header */}
      <header className="border-b border-border bg-surface-1 sticky top-0 z-40">
        <div className="max-w-5xl mx-auto px-4 h-14 flex items-center gap-6">
          <Link to="/dashboard" className="text-sm font-semibold tracking-tight text-white">
            Dhurta Host
          </Link>

          <nav className="flex items-center gap-1 ml-2">
            {navLinks.map((link) => (
              <Link
                key={link.to}
                to={link.to}
                className={`px-3 py-1.5 rounded-md text-sm transition-colors ${
                  location.pathname.startsWith(link.to)
                    ? 'text-white bg-surface-3'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-surface-2'
                }`}
              >
                {link.label}
              </Link>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            {device && (
              <span className="text-xs text-zinc-500 hidden sm:block">
                {device.name}
              </span>
            )}
            <button
              onClick={handleLogout}
              className="text-xs text-zinc-400 hover:text-zinc-200 px-2 py-1.5 rounded transition-colors"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      {/* Page content */}
      <main className="flex-1 max-w-5xl mx-auto w-full px-4 py-8">
        {title && (
          <h1 className="text-xl font-semibold text-white mb-6">{title}</h1>
        )}
        {children}
      </main>
    </div>
  )
}
