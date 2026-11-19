export default function ComingSoon({ title }: { title: string }) {
  return (
    <section className="py-16 text-center">
      <h1 className="text-4xl">{title}</h1>
      <p className="text-ink-muted mt-4 text-lg">Coming Soon!</p>
    </section>
  )
}
