import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useEffect } from 'react'
import { isAuthenticated, getEndpoint } from './services/auth'
import { useAuthStore } from './stores'
import Landing from './pages/Landing'
import Auth from './pages/Auth'
import Dashboard from './pages/Dashboard'
import Workspace from './pages/Workspace'
import Settings from './pages/Settings'
import Help from './pages/Help'
import ProtectedRoute from './components/ProtectedRoute'

export default function App() {
  const setAuthenticated = useAuthStore((s) => s.setAuthenticated)
  const setEndpoint = useAuthStore((s) => s.setEndpoint)

  useEffect(() => {
    setAuthenticated(isAuthenticated())
    const ep = getEndpoint()
    if (ep) setEndpoint(ep)
  }, [setAuthenticated, setEndpoint])

  const base = import.meta.env.VITE_BASE_PATH ?? '/'

  return (
    <BrowserRouter basename={base}>
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/auth" element={<Auth />} />
        <Route path="/help" element={<Help />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/workspace/:deviceId" element={<Workspace />} />
          <Route path="/settings" element={<Settings />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
