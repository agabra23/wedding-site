import type { Role } from '../api/types'

export type Stored = { token: string; role: Role; exp: number }
const KEY = 'wedding-session'

/** Reads the `exp` claim (seconds) from a JWT payload. */
export function readExp(token: string): number | null {
  try {
    const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const exp = JSON.parse(atob(b64)).exp
    return typeof exp === 'number' ? exp : null
  } catch {
    return null
  }
}

export function loadSession(): Stored | null {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return null
    const s = JSON.parse(raw) as Stored
    if (!s.token || s.exp * 1000 <= Date.now()) {
      localStorage.removeItem(KEY)
      return null
    }
    return s
  } catch {
    return null
  }
}

export function saveSession(s: Stored | null) {
  try {
    if (s) localStorage.setItem(KEY, JSON.stringify(s))
    else localStorage.removeItem(KEY)
  } catch {
    /* storage unavailable: session lasts until reload */
  }
}
