import { useState } from 'react'

const FAQS = [
  { q: 'What should I wear?', a: 'Cocktail attire. The ceremony is outdoors, so plan for grass.' },
  { q: 'Can I bring a plus one?', a: 'Your invitation lists everyone invited in your party.' },
  { q: 'Are kids welcome?', a: 'We love your little ones, but this is an adults-only celebration.' },
  { q: 'Will there be parking?', a: 'Yes, free on-site parking is available at the venue.' },
  { q: 'When is the RSVP deadline?', a: 'Please reply by May 1, 2027.' },
]

function FaqItem({ q, a }: { q: string; a: string }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="border-border border-b">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className="hover:text-ink-muted flex w-full cursor-pointer items-center justify-between gap-4 py-4 text-left text-lg transition-colors duration-200"
      >
        {q}
        <svg
          viewBox="0 0 24 24"
          className={`size-5 shrink-0 transition-transform duration-300 ease-out ${open ? 'rotate-180' : ''}`}
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {/* grid-rows 0fr -> 1fr animates height smoothly without measuring content */}
      <div
        className={`grid transition-[grid-template-rows,opacity] duration-300 ease-out ${
          open ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0'
        }`}
      >
        <div className="overflow-hidden">
          <p className="text-ink-muted pb-4">{a}</p>
        </div>
      </div>
    </div>
  )
}

export default function Faq() {
  return (
    <section className="py-8">
      <h1 className="text-4xl">FAQ</h1>
      <div className="mt-6">
        {FAQS.map((f) => (
          <FaqItem key={f.q} {...f} />
        ))}
      </div>
    </section>
  )
}
