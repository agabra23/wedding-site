import type { ComponentProps } from 'react'

export default function Button({ className = '', type = 'button', ...props }: ComponentProps<'button'>) {
  return <button type={type} className={`btn ${className}`} {...props} />
}
