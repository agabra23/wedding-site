import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { signIn as apiSignIn } from '../api/endpoints'
import { setUnauthorizedHandler } from '../api/client'
import type { Role } from '../api/types'
import { loadSession, readExp, saveSession, type Stored } from './storage'

type Session = {
  role: Role | null
  token: string | null
  signIn: (password: string) => Promise<void>
  signOut: () => void
}

const Ctx = createContext<Session | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [stored, setStored] = useState<Stored | null>(loadSession)

  const signOut = useCallback(() => {
    saveSession(null)
    setStored(null)
  }, [])

  const signIn = useCallback(async (password: string) => {
    const { role, token } = await apiSignIn(password)
    const s: Stored = { role, token, exp: readExp(token) ?? Math.floor(Date.now() / 1000) + 3600 }
    saveSession(s)
    setStored(s)
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(signOut)
    return () => setUnauthorizedHandler(null)
  }, [signOut])

  // Sign out when the token expires while the page is open.
  useEffect(() => {
    if (!stored) return
    const ms = Math.min(stored.exp * 1000 - Date.now(), 2 ** 31 - 1)
    const t = setTimeout(signOut, Math.max(ms, 0))
    return () => clearTimeout(t)
  }, [stored, signOut])

  const value = useMemo<Session>(
    () => ({ role: stored?.role ?? null, token: stored?.token ?? null, signIn, signOut }),
    [stored, signIn, signOut],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useSession() {
  const s = useContext(Ctx)
  if (!s) throw new Error('useSession must be used inside SessionProvider')
  return s
}
