import { Link } from 'react-router-dom'

export default function NotFound() {
  return (
    <section className="py-16 text-center">
      <h1 className="text-4xl">Page not found</h1>
      <Link to="/" className="text-ink-muted mt-4 inline-block underline">
        Back home
      </Link>
    </section>
  )
}
