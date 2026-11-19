import { Link } from 'react-router-dom'

export default function Home() {
  return (
    <section>
      {/* Full-bleed hero: breaks out of the centered <main>. Height scales with the
          viewport (svh = mobile-safe) but is clamped so tiny and ultrawide screens
          both look right. object-cover crops instead of stretching; change
          object-center to move the focal point (e.g. object-top). */}
      <div className="relative left-1/2 -ml-[50vw] h-[60svh] max-h-[48rem] min-h-80 w-screen">
        <img
          src="/hero.svg"
          alt="Us"
          width={1600}
          height={900}
          fetchPriority="high"
          decoding="async"
          className="size-full object-cover object-center"
        />
        {/* Scrim keeps overlay text readable on any photo; tune the /40 opacity. */}
        <div className="bg-ink/40 absolute inset-0" aria-hidden="true" />
        <div className="text-on-primary absolute inset-0 flex flex-col items-center justify-center px-4 text-center">
          <h1 className="text-on-primary text-5xl md:text-7xl">Our Wedding</h1>
          <p className="mt-2">June 1, 2027 · Somewhere Beautiful</p>
          <Link to="/rsvp" className="bg-surface text-ink hover:bg-background mt-6 inline-block rounded px-6 py-3 transition-colors duration-200">
            RSVP
          </Link>
        </div>
      </div>
    </section>
  )
}
