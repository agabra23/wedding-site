import { useState, type FormEvent } from 'react'
import { getParty, submitRsvp, type Guest, type Party } from '../api'

export default function Rsvp() {
  const [code, setCode] = useState('')
  const [party, setParty] = useState<Party | null>(null)
  const [guests, setGuests] = useState<Guest[]>([])
  const [email, setEmail] = useState('')
  const [status, setStatus] = useState<'idle' | 'loading' | 'done'>('idle')
  const [error, setError] = useState<string | null>(null)

  async function lookup(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setStatus('loading')
    try {
      const p = await getParty(code)
      if (!p) {
        setError('We could not find that invite code.')
      } else {
        setParty(p)
        setGuests(p.guests)
        setEmail(p.email ?? '')
      }
    } catch (err) {
      setError((err as Error).message)
    }
    setStatus('idle')
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!party) return
    setError(null)
    setStatus('loading')
    try {
      await submitRsvp(party.id, email || null, guests)
      setStatus('done')
    } catch (err) {
      setError((err as Error).message)
      setStatus('idle')
    }
  }

  function update(i: number, patch: Partial<Guest>) {
    setGuests((gs) => gs.map((g, idx) => (idx === i ? { ...g, ...patch } : g)))
  }

  if (status === 'done') {
    return <p className="py-16 text-center text-lg">Thank you! Your RSVP is saved.</p>
  }

  if (!party) {
    return (
      <form onSubmit={lookup} className="space-y-4 py-8">
        <h1 className="font-serif text-3xl">RSVP</h1>
        <label className="block">
          Invite code
          <input
            className="mt-1 block w-full rounded border border-stone-300 p-2 uppercase"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
          />
        </label>
        <button className="rounded bg-stone-800 px-4 py-2 text-white" disabled={status === 'loading'}>
          Find my invitation
        </button>
        {error && <p className="text-red-700">{error}</p>}
      </form>
    )
  }

  return (
    <form onSubmit={save} className="space-y-6 py-8">
      <h1 className="font-serif text-3xl">{party.partyName}</h1>
      {guests.map((g, i) => (
        <fieldset key={i} className="space-y-2 rounded border border-stone-200 bg-white p-4">
          <legend className="px-1 font-medium">{g.name}</legend>
          <div className="flex gap-4">
            {(['yes', 'no'] as const).map((v) => (
              <label key={v}>
                <input
                  type="radio"
                  name={`rsvp-${i}`}
                  checked={g.rsvp === v}
                  onChange={() => update(i, { rsvp: v })}
                  required
                />{' '}
                {v === 'yes' ? 'Joyfully accepts' : 'Regretfully declines'}
              </label>
            ))}
          </div>
          {g.rsvp === 'yes' && (
            <input
              className="block w-full rounded border border-stone-300 p-2"
              placeholder="Meal choice"
              value={g.meal ?? ''}
              onChange={(e) => update(i, { meal: e.target.value || null })}
            />
          )}
        </fieldset>
      ))}
      <label className="block">
        Contact email
        <input
          type="email"
          className="mt-1 block w-full rounded border border-stone-300 p-2"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </label>
      <button className="rounded bg-stone-800 px-4 py-2 text-white" disabled={status === 'loading'}>
        Submit RSVP
      </button>
      {error && <p className="text-red-700">{error}</p>}
    </form>
  )
}
