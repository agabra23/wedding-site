import { useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import { lookup, submitRsvp } from '../api/endpoints'
import type { Party, Rsvp as RsvpValue } from '../api/types'
import Button from '../components/form/Button'
import Radio from '../components/form/Radio'
import TextInput from '../components/form/TextInput'
import Notice from '../components/Notice'

type Answers = Record<string, RsvpValue>
type Step = 'find' | 'answer' | 'done'

const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'long', timeStyle: 'short' })
const label = (r: RsvpValue) => (r === 'attending' ? 'Attending' : r === 'declined' ? 'Declining' : 'No answer yet')

export default function Rsvp() {
  const [step, setStep] = useState<Step>('find')
  const [first, setFirst] = useState('')
  const [last, setLast] = useState('')
  const [party, setParty] = useState<Party | null>(null)
  const [deadline, setDeadline] = useState<string | null>(null)
  const [answers, setAnswers] = useState<Answers>({})
  const [locked, setLocked] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  const load = (p: Party, d: string | null) => {
    setParty(p)
    setDeadline(d)
    setAnswers(Object.fromEntries(p.guests.map((g) => [g.id, g.rsvp])))
    setLocked(d !== null && new Date(d).getTime() <= Date.now())
  }

  const find = async (e: FormEvent) => {
    e.preventDefault()
    setPending(true)
    setError(null)
    setInfo(null)
    try {
      const res = await lookup(first, last)
      load(res.party, res.rsvpDeadline)
      setStep('answer')
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 404
          ? "We couldn't find that name. Check the spelling, or contact us."
          : err instanceof ApiError && err.status === 429
            ? 'Too many tries. Wait a minute and try again.'
            : err instanceof ApiError ? err.message : 'Something went wrong. Please try again.',
      )
    } finally {
      setPending(false)
    }
  }

  const answered = party?.guests.filter((g) => answers[g.id] !== 'pending') ?? []

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!party) return
    setError(null)
    setInfo(null)
    if (answered.length === 0) {
      setError('Answer attending or declining for at least one guest.')
      return
    }
    setPending(true)
    try {
      await submitRsvp(
        party.id,
        answered.map((g) => ({ guestId: g.id, rsvp: answers[g.id] as 'attending' | 'declined' })),
      )
      // Keep the saved values so "Change answers" shows them.
      setParty({ ...party, guests: party.guests.map((g) => ({ ...g, rsvp: answers[g.id] })) })
      setStep('done')
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setLocked(true)
        setError('The RSVP deadline has passed. Contact us to make changes.')
      } else if (err instanceof ApiError && err.status === 409) {
        setInfo('Someone in your party just saved. We reloaded the latest answers; please review and submit again.')
        try {
          const res = await lookup(first, last)
          load(res.party, res.rsvpDeadline)
        } catch {
          setError('Could not reload your party. Please start again.')
        }
      } else {
        setError(err instanceof ApiError ? err.message : 'Something went wrong. Please try again.')
      }
    } finally {
      setPending(false)
    }
  }

  const reset = () => {
    setStep('find')
    setParty(null)
    setError(null)
    setInfo(null)
  }

  return (
    <section className="py-8">
      <h1 className="text-4xl">RSVP</h1>

      {step === 'find' && (
        <form key="find" onSubmit={find} className="animate-fade-in mt-6 space-y-4">
          <label className="block">
            First name
            <TextInput value={first} onChange={(e) => setFirst(e.target.value)} className="mt-1" required autoComplete="given-name" />
          </label>
          <label className="block">
            Last name
            <TextInput value={last} onChange={(e) => setLast(e.target.value)} className="mt-1" required autoComplete="family-name" />
          </label>
          {error && <Notice kind="error">{error}</Notice>}
          <Button type="submit" pending={pending}>
            Find my invitation
          </Button>
        </form>
      )}

      {step === 'answer' && party && (
        <form key="answer" onSubmit={submit} className="animate-fade-in mt-6 space-y-4">
          {deadline && (
            <p className="text-ink-muted normal-case">
              Please reply by {fmt(deadline)}.
            </p>
          )}
          {locked && <Notice>The RSVP deadline has passed. Contact us to make changes.</Notice>}
          {info && <Notice>{info}</Notice>}
          {party.guests.map((g) => (
            <fieldset key={g.id} disabled={locked} className="border-border rounded border p-4">
              <legend className="px-2">
                {g.firstName} {g.lastName}
              </legend>
              <div className="flex flex-wrap gap-x-6">
                {(['attending', 'declined'] as const).map((v) => (
                  <Radio
                    key={v}
                    name={`guest-${g.id}`}
                    label={label(v)}
                    checked={answers[g.id] === v}
                    onChange={() => setAnswers((a) => ({ ...a, [g.id]: v }))}
                  />
                ))}
              </div>
            </fieldset>
          ))}
          {error && <Notice kind="error">{error}</Notice>}
          <div className="flex flex-wrap gap-3">
            <Button type="button" className="bg-transparent text-primary border-border border hover:bg-surface" onClick={reset}>
              Back
            </Button>
            {!locked && (
              <Button type="submit" pending={pending}>
                Submit RSVP
              </Button>
            )}
          </div>
        </form>
      )}

      {step === 'done' && party && (
        <div key="done" className="animate-fade-in mt-6 space-y-4">
          <p>Thank you! Your reply has been recorded.</p>
          <ul className="space-y-1">
            {party.guests.map((g) => (
              <li key={g.id}>
                {g.firstName} {g.lastName}: {label(g.rsvp)}
              </li>
            ))}
          </ul>
          <Button onClick={() => setStep('answer')}>Change answers</Button>
        </div>
      )}
    </section>
  )
}
