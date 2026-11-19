import { Link } from 'react-router-dom'

export default function Home() {
  return (
    <section className="py-16 text-center">
      <h1 className="font-serif text-4xl">We&apos;re getting married</h1>
      <p className="mt-4 text-stone-600">Date and venue details go here.</p>
      <Link
        to="/rsvp"
        className="mt-8 inline-block rounded bg-stone-800 px-6 py-3 text-white"
      >
        RSVP
      </Link>
    </section>
  )
}
