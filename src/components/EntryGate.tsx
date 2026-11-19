import { useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import { useSession } from '../auth/session'
import Button from './form/Button'
import TextInput from './form/TextInput'
import Notice from './Notice'

function message(e: unknown) {
  if (e instanceof ApiError) {
    if (e.status === 401) return "That password doesn't match. Check your invitation."
    if (e.status === 429) return 'Too many tries. Wait a minute and try again.'
  }
  return 'Something went wrong. Check your connection and try again.'
}

export default function EntryGate() {
  const { signIn } = useSession()
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setPending(true)
    setError(null)
    try {
      await signIn(password)
    } catch (err) {
      setError(message(err))
      setPending(false)
    }
  }

  return (
    <main className="grid min-h-screen place-items-center px-4">
      <form onSubmit={submit} className="animate-fade-in w-full max-w-sm space-y-4 text-center">
        <h1 className="font-display text-5xl">Our Wedding</h1>
        <label className="block text-left">
          Password
          <TextInput
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1"
            required
            autoFocus
          />
        </label>
        {error && <Notice kind="error">{error}</Notice>}
        <Button type="submit" pending={pending} className="w-full">
          Enter
        </Button>
      </form>
    </main>
  )
}
