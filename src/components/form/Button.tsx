import type { ComponentProps } from 'react'

type Props = ComponentProps<'button'> & { pending?: boolean }

export default function Button({ className = '', type = 'button', pending, disabled, ...props }: Props) {
  return (
    <button
      type={type}
      className={`btn ${className}`}
      disabled={disabled || pending}
      aria-busy={pending || undefined}
      {...props}
    />
  )
}
