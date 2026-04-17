import { cn } from '../../lib/utils'

const buttonVariants = {
  default: 'ui-button ui-button--primary',
  secondary: 'ui-button ui-button--secondary',
  ghost: 'ui-button ui-button--ghost',
}

const buttonSizes = {
  default: 'ui-button--md',
  sm: 'ui-button--sm',
  lg: 'ui-button--lg',
}

export function Button({ className = '', variant = 'default', size = 'default', type = 'button', ...props }) {
  return (
    <button
      type={type}
      {...props}
      className={cn(buttonVariants[variant] ?? buttonVariants.default, buttonSizes[size] ?? buttonSizes.default, className)}
    />
  )
}
