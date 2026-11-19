export type Rsvp = 'pending' | 'attending' | 'declined'
export type Role = 'admin' | 'guest'

export type Guest = {
  id: string
  firstName: string
  lastName: string
  rsvp: Rsvp
  rsvpChangedAt: string | null
  rsvpChangedBy: string | null
}

export type Party = { id: string; guests: Guest[] }

export type AuthResponse = { role: Role; token: string }
export type LookupResponse = { party: Party; rsvpDeadline: string | null }
export type Settings = { rsvpDeadline: string | null }
export type RsvpAnswer = { guestId: string; rsvp: Exclude<Rsvp, 'pending'> }
export type GuestName = { firstName: string; lastName: string }
