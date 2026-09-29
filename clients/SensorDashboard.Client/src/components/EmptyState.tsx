import type { ReactNode } from 'react'

/** A clear "nothing here yet" with what it means and, optionally, what to do about it. */
export function EmptyState({ icon = '○', title, children, action }: {
  icon?: string
  title: string
  children?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="card empty-state">
      <div className="empty-icon" aria-hidden>{icon}</div>
      <h2>{title}</h2>
      {children && <p className="muted">{children}</p>}
      {action}
    </div>
  )
}
