import type { ComponentProps, ReactNode } from 'react'

type Props = Omit<ComponentProps<'input'>, 'type'> & { label: ReactNode; labelClassName?: string }

/** `className` styles the control; `labelClassName` styles the wrapping label. */
export default function Radio({ label, className = '', labelClassName = '', ...props }: Props) {
  return (
    <label className={`choice ${labelClassName}`}>
      <input type="radio" className={`radio ${className}`} {...props} />
      <span>{label}</span>
    </label>
  )
}
