import type { ComponentProps } from 'react'

export default function Select({ className = '', ...props }: ComponentProps<'select'>) {
  return <select className={`select ${className}`} {...props} />
}
