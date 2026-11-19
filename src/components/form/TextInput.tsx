import type { ComponentProps } from 'react'

export default function TextInput({ className = '', type = 'text', ...props }: ComponentProps<'input'>) {
  return <input type={type} className={`input ${className}`} {...props} />
}
