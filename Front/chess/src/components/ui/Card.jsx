import { cn } from '../../lib/utils'

export function Card({ className = '', ...props }) {
  return <div {...props} className={cn('ui-card', className)} />
}

export function CardHeader({ className = '', ...props }) {
  return <div {...props} className={cn('ui-card__header', className)} />
}

export function CardTitle({ className = '', ...props }) {
  return <h2 {...props} className={cn('ui-card__title', className)} />
}

export function CardDescription({ className = '', ...props }) {
  return <p {...props} className={cn('ui-card__description', className)} />
}

export function CardContent({ className = '', ...props }) {
  return <div {...props} className={cn('ui-card__content', className)} />
}
