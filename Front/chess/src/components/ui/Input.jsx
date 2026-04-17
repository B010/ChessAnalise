import { cn } from '../../lib/utils'

export function Input({ className = '', ...props }) {
  return (
    <input
      {...props}
      className={cn('ui-input', className)}
    />
  )
}
