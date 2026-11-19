import type { ReactNode } from 'react'

export default function Notice({ kind = 'info', children }: { kind?: 'error' | 'info'; children: ReactNode }) {
  return (
    <p
      role={kind === 'error' ? 'alert' : 'status'}
      className={`animate-fade-in border-border bg-surface rounded border px-3 py-2 normal-case ${
        kind === 'error' ? 'text-error' : 'text-ink-muted'
      }`}
    >
      {children}
    </p>
  )
}
