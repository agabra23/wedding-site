import { useEffect, useState } from 'react'
import { adminFetch } from '../auth'
import type { Party } from '../api'

export default function Admin() {
  const [parties, setParties] = useState<Party[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    adminFetch('/admin/parties')
      .then(async (res) => {
        if (!res) return
        if (res.status === 403) return setError('Your account is not on the admin list.')
        if (!res.ok) return setError(`Request failed (${res.status})`)
        setParties(await res.json())
      })
      .catch((e: Error) => setError(e.message))
  }, [])

  if (error) return <p className="py-8 text-red-700">{error}</p>
  if (!parties) return <p className="py-8">Loading…</p>

  return (
    <div className="py-8">
      <h1 className="font-serif text-3xl">Guest list</h1>
      <table className="mt-4 w-full text-left text-sm">
        <thead>
          <tr className="border-b">
            <th className="p-2">Code</th>
            <th className="p-2">Party</th>
            <th className="p-2">Responses</th>
          </tr>
        </thead>
        <tbody>
          {parties.map((p) => (
            <tr key={p.id} className="border-b">
              <td className="p-2 font-mono">{p.id}</td>
              <td className="p-2">{p.partyName}</td>
              <td className="p-2">{p.guests.map((g) => `${g.name}: ${g.rsvp}`).join(', ')}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
