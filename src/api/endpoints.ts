import { request } from './client'
import type { AuthResponse, GuestName, LookupResponse, Party, Rsvp, RsvpAnswer, Settings } from './types'

export const signIn = (password: string) => request<AuthResponse>('POST', '/auth', { password })

// #23
export const logout = () => request<void>('POST', '/auth/logout')

export const lookup = (firstName: string, lastName: string) =>
  request<LookupResponse>('POST', '/lookup', { firstName, lastName })

export const submitRsvp = (partyId: string, answers: RsvpAnswer[]) =>
  request<void>('POST', `/parties/${encodeURIComponent(partyId)}/rsvp`, { answers })

export const listParties = () => request<Party[]>('GET', '/admin/parties')

export const addGuests = (req: { partyId?: string; guests: GuestName[] }) =>
  request<Party>('POST', '/admin/guests', req)

export const getSettings = () => request<Settings>('GET', '/admin/settings')

export const setDeadline = (iso: string | null) =>
  request<Settings>('PUT', '/admin/settings', { rsvpDeadline: iso })

const guestPath = (id: string) => `/admin/guests/${encodeURIComponent(id)}`

// #10
export const setGuestRsvp = (guestId: string, rsvp: Rsvp) =>
  request<Party>('PUT', `${guestPath(guestId)}/rsvp`, { rsvp })

// #11
export const renameGuest = (guestId: string, name: { firstName?: string; lastName?: string }) =>
  request<unknown>('PATCH', guestPath(guestId), name)
export const removeGuest = (guestId: string) => request<void>('DELETE', guestPath(guestId))

// #12
export const moveGuest = (guestId: string, partyId?: string) =>
  request<Party>('POST', `${guestPath(guestId)}/move`, { partyId })
