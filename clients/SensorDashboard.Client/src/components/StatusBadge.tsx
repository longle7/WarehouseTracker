import type { SensorStatus } from '../api/types'
import { STATUS } from './status'

/** Status is always shown as color + icon + label, never color alone. */
export function StatusBadge({ status }: { status: SensorStatus }) {
  const { label, icon, color } = STATUS[status]
  return (
    <span className="badge">
      <span className="dot" style={{ background: color }} aria-hidden />
      <span className="icon" aria-hidden>
        {icon}
      </span>
      {label}
    </span>
  )
}
