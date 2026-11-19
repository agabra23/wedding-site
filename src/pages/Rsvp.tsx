import { useState, type FormEvent } from 'react'
import Button from '../components/form/Button'
import Radio from '../components/form/Radio'
import TextInput from '../components/form/TextInput'

// TODO: replace with API calls (find Party by full name, save RSVPs).
type Guest = { name: string; attending: boolean | null }

const MOCK_PARTY: Guest[] = [
  { name: 'Jane Doe', attending: null },
  { name: 'John Doe', attending: null },
]

export default function Rsvp() {
  const [name, setName] = useState('')
  const [party, setParty] = useState<Guest[] | null>(null)
  const [submitted, setSubmitted] = useState(false)

  const find = (e: FormEvent) => {
    e.preventDefault()
    setParty(MOCK_PARTY)
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
  }

  const setAttending = (i: number, attending: boolean | null) =>
    setParty((p) => p && p.map((g, j) => (j === i ? { ...g, attending } : g)))

  return (
    <section className="py-8">
      <h1 className="text-4xl">RSVP</h1>

      {submitted ? (
        <p className="animate-fade-in mt-6">Thank you! Your reply has been recorded.</p>
      ) : !party ? (
        <form onSubmit={find} className="animate-fade-in mt-6 space-y-4">
          <label className="block">
            Full name
            <TextInput value={name} onChange={(e) => setName(e.target.value)} className="mt-1" required />
          </label>
          <Button type="submit">Find my invitation</Button>
        </form>
      ) : (
        <form onSubmit={submit} className="animate-fade-in mt-6 space-y-4">
          {party.map((g, i) => (
            <fieldset key={g.name} className="border-border rounded border p-4">
              <legend className="px-2">{g.name}</legend>
              <div className="flex flex-wrap gap-x-6">
                <Radio
                  name={`guest-${i}`}
                  label="Attending"
                  checked={g.attending === true}
                  onChange={() => {}}
                  onClick={() => setAttending(i, g.attending === true ? null : true)}
                />
                <Radio
                  name={`guest-${i}`}
                  label="Declining"
                  checked={g.attending === false}
                  onChange={() => {}}
                  onClick={() => setAttending(i, g.attending === false ? null : false)}
                />
              </div>
            </fieldset>
          ))}
          <div className="flex flex-wrap gap-3">
            <Button type="button" className="bg-transparent text-primary border-border border hover:bg-surface" onClick={() => setParty(null)}>
              Back
            </Button>
            <Button type="submit">Submit RSVP</Button>
          </div>
        </form>
      )}
    </section>
  )
}
