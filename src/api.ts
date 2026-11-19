export type Guest = {
  name: string
  rsvp: 'pending' | 'yes' | 'no'
  meal: string | null
  isPlusOne: boolean
}

export type Party = {
  id: string
  partyName: string
  email: string | null
  guests: Guest[]
  respondedAt: string | null
}

const base = import.meta.env.VITE_API_URL

export async function getParty(code: string): Promise<Party | null> {
  const res = await fetch(`${base}/party/${encodeURIComponent(code.trim())}`)
  if (res.status === 404) return null
  if (!res.ok) throw new Error(`Lookup failed (${res.status})`)
  return res.json()
}

export async function submitRsvp(
  code: string,
  email: string | null,
  guests: Pick<Guest, 'name' | 'rsvp' | 'meal'>[],
): Promise<void> {
  const res = await fetch(`${base}/rsvp/${encodeURIComponent(code)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, guests }),
  })
  if (!res.ok) throw new Error(`RSVP failed (${res.status})`)
}
