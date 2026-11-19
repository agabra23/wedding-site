import type { ReactNode } from 'react'
import { useSession } from '../auth/session'
import NotFound from '../pages/NotFound'

export default function RequireAdmin({ children }: { children: ReactNode }) {
  const { role } = useSession()
  return role === 'admin' ? <>{children}</> : <NotFound />
}
