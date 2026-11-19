import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import * as api from '../api/endpoints'
import type { Guest, GuestName, Party, Rsvp } from '../api/types'
import Button from '../components/form/Button'
import Select from '../components/form/Select'
import TextInput from '../components/form/TextInput'
import Notice from '../components/Notice'

const errMsg = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const fullName = (g: Guest) => `${g.firstName} ${g.lastName}`
const partyLabel = (p: Party) => p.guests.map(fullName).join(', ')
const ghost = 'bg-transparent text-primary border-border border hover:bg-surface'

function relative(iso: string) {
  const s = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  if (s < 60) return rtf.format(0, 'second')
  if (s < 3600) return rtf.format(-Math.floor(s / 60), 'minute')
  if (s < 86400) return rtf.format(-Math.floor(s / 3600), 'hour')
  return rtf.format(-Math.floor(s / 86400), 'day')
}

const toLocalInput = (iso: string | null) => {
  if (!iso) return ''
  const d = new Date(iso)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

export default function Admin() {
  const [parties, setParties] = useState<Party[] | null>(null)
  const [deadline, setDeadline] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refetch = useCallback(async () => {
    try {
      const [p, s] = await Promise.all([api.listParties(), api.getSettings()])
      setParties(p)
      setDeadline(s.rsvpDeadline)
      setError(null)
    } catch (e) {
      setError(errMsg(e))
    }
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  const counts = useMemo(() => {
    const all = (parties ?? []).flatMap((p) => p.guests)
    const n = (r: Rsvp) => all.filter((g) => g.rsvp === r).length
    return { attending: n('attending'), declined: n('declined'), pending: n('pending'), total: all.length }
  }, [parties])

  return (
    <section className="space-y-8 py-8">
      <h1 className="text-4xl">Admin</h1>
      {error && <Notice kind="error">{error}</Notice>}
      {!parties ? (
        !error && <p className="text-ink-muted animate-fade-in">Loading…</p>
      ) : (
        <div className="animate-fade-in space-y-8">
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {Object.entries(counts).map(([k, v]) => (
              <div key={k} className="border-border bg-surface rounded border p-3">
                <dt className="text-ink-muted text-sm capitalize">{k}</dt>
                <dd className="text-2xl">{v}</dd>
              </div>
            ))}
          </dl>
          <DeadlineSection deadline={deadline} onSaved={setDeadline} />
          <AddGuests parties={parties} onAdded={refetch} />
          <div className="space-y-3">
            <h2 className="text-2xl">Parties</h2>
            {parties.length === 0 && <p className="text-ink-muted">No parties yet.</p>}
            {parties.map((p) => (
              <PartyCard key={p.id} party={p} parties={parties} onChanged={refetch} />
            ))}
          </div>
        </div>
      )}
    </section>
  )
}

function DeadlineSection({ deadline, onSaved }: { deadline: string | null; onSaved: (d: string | null) => void }) {
  const [value, setValue] = useState(toLocalInput(deadline))
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = async (iso: string | null) => {
    setPending(true)
    setError(null)
    try {
      const res = await api.setDeadline(iso)
      onSaved(res.rsvpDeadline)
      setValue(toLocalInput(res.rsvpDeadline))
    } catch (e) {
      setError(errMsg(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="space-y-3">
      <h2 className="text-2xl">RSVP Deadline</h2>
      <p className="text-ink-muted normal-case">
        Current: {deadline ? new Date(deadline).toLocaleString() : 'open (no deadline)'}
      </p>
      <TextInput type="datetime-local" value={value} onChange={(e) => setValue(e.target.value)} aria-label="RSVP deadline" />
      {error && <Notice kind="error">{error}</Notice>}
      <div className="flex flex-wrap gap-3">
        <Button pending={pending} disabled={!value} onClick={() => save(new Date(value).toISOString())}>
          Save
        </Button>
        <Button pending={pending} className={ghost} onClick={() => save(null)}>
          Clear
        </Button>
      </div>
    </div>
  )
}

function AddGuests({ parties, onAdded }: { parties: Party[]; onAdded: () => void }) {
  const blank = (): GuestName => ({ firstName: '', lastName: '' })
  const [rows, setRows] = useState<GuestName[]>([blank()])
  const [partyId, setPartyId] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const update = (i: number, patch: Partial<GuestName>) =>
    setRows((r) => r.map((x, j) => (j === i ? { ...x, ...patch } : x)))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.addGuests({ partyId: partyId || undefined, guests: rows })
      setRows([blank()])
      setPartyId('')
      onAdded()
    } catch (err) {
      setError(errMsg(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <h2 className="text-2xl">Add guests</h2>
      {rows.map((r, i) => (
        <div key={i} className="flex flex-wrap items-center gap-2 sm:flex-nowrap">
          <TextInput placeholder="First name" aria-label="First name" value={r.firstName} onChange={(e) => update(i, { firstName: e.target.value })} required />
          <TextInput placeholder="Last name" aria-label="Last name" value={r.lastName} onChange={(e) => update(i, { lastName: e.target.value })} required />
          {rows.length > 1 && (
            <Button className={`${ghost} px-3`} aria-label="Remove row" onClick={() => setRows((x) => x.filter((_, j) => j !== i))}>
              ✕
            </Button>
          )}
        </div>
      ))}
      <Button className={ghost} onClick={() => setRows((r) => [...r, blank()])}>
        + another person
      </Button>
      <Select value={partyId} onChange={(e) => setPartyId(e.target.value)} aria-label="Party">
        <option value="">New party</option>
        {parties.map((p) => (
          <option key={p.id} value={p.id}>
            {partyLabel(p)}
          </option>
        ))}
      </Select>
      {error && <Notice kind="error">{error}</Notice>}
      <Button type="submit" pending={pending}>
        Add
      </Button>
    </form>
  )
}

function PartyCard({ party, parties, onChanged }: { party: Party; parties: Party[]; onChanged: () => void }) {
  return (
    <div className="border-border bg-surface space-y-4 rounded border p-4">
      {party.guests.map((g) => (
        <GuestRow key={g.id} guest={g} party={party} parties={parties} onChanged={onChanged} />
      ))}
    </div>
  )
}

function GuestRow({ guest, party, parties, onChanged }: { guest: Guest; party: Party; parties: Party[]; onChanged: () => void }) {
  const [renaming, setRenaming] = useState(false)
  const [first, setFirst] = useState(guest.firstName)
  const [last, setLast] = useState(guest.lastName)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = async (fn: () => Promise<unknown>, after?: () => void) => {
    setPending(true)
    setError(null)
    try {
      await fn()
      after?.()
      onChanged()
    } catch (e) {
      setError(errMsg(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="border-border space-y-2 border-b pb-4 last:border-b-0 last:pb-0">
      <div>
        <p className="text-lg">{fullName(guest)}</p>
        <p className="text-ink-muted text-sm normal-case">
          {guest.rsvp}
          {guest.rsvpChangedAt && ` · changed by ${guest.rsvpChangedBy ?? 'unknown'} · ${relative(guest.rsvpChangedAt)}`}
        </p>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        <Select
          aria-label={`RSVP for ${fullName(guest)}`}
          value={guest.rsvp}
          disabled={pending}
          onChange={(e) => run(() => api.setGuestRsvp(guest.id, e.target.value as Rsvp))}
        >
          <option value="pending">pending</option>
          <option value="attending">attending</option>
          <option value="declined">declined</option>
        </Select>
        <Select
          aria-label={`Move ${fullName(guest)}`}
          value=""
          disabled={pending}
          onChange={(e) => e.target.value && run(() => api.moveGuest(guest.id, e.target.value === '__own' ? undefined : e.target.value))}
        >
          <option value="">Move to…</option>
          {party.guests.length > 1 && <option value="__own">Own party</option>}
          {parties
            .filter((p) => p.id !== party.id)
            .map((p) => (
              <option key={p.id} value={p.id}>
                {partyLabel(p)}
              </option>
            ))}
        </Select>
      </div>
      {renaming && (
        <div className="flex flex-wrap items-center gap-2 sm:flex-nowrap">
          <TextInput aria-label="First name" value={first} onChange={(e) => setFirst(e.target.value)} />
          <TextInput aria-label="Last name" value={last} onChange={(e) => setLast(e.target.value)} />
          <Button
            pending={pending}
            onClick={() => run(() => api.renameGuest(guest.id, { firstName: first, lastName: last }), () => setRenaming(false))}
          >
            Save
          </Button>
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        <Button className={ghost} onClick={() => setRenaming((r) => !r)}>
          {renaming ? 'Cancel' : 'Rename'}
        </Button>
        <Button
          className={ghost}
          disabled={pending}
          onClick={() => confirm(`Remove ${fullName(guest)}?`) && run(() => api.removeGuest(guest.id))}
        >
          Remove
        </Button>
      </div>
      {error && <Notice kind="error">{error}</Notice>}
    </div>
  )
}
