const SECTIONS = [
  { title: 'Getting there', body: 'Fly into City International Airport (ABC), about 40 minutes from the venue.' },
  { title: 'Where to stay', body: 'A room block is held at the Grand Hotel. Mention our names when booking.' },
  { title: 'Getting around', body: 'Rideshare is plentiful. A shuttle will run between the hotel and venue.' },
]

export default function Travel() {
  return (
    <section className="py-8">
      <h1 className="text-4xl">Travel</h1>
      <div className="mt-6 space-y-6">
        {SECTIONS.map((s) => (
          <div key={s.title}>
            <h2 className="text-2xl">{s.title}</h2>
            <p className="text-ink-muted mt-1">{s.body}</p>
          </div>
        ))}
      </div>
    </section>
  )
}
