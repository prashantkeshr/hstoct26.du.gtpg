import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '../stores'

export default function ProtectedRoute() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  return isAuthenticated ? <Outlet /> : <Navigate to="/auth" replace />
}
