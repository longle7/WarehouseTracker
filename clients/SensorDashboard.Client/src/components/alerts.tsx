import { describeError } from '../api/errorMessages'
import { useAcknowledgeAlert } from '../api/queries'
import type { Alert, AlertSeverity, AlertState } from '../api/types'

const SEVERITY: Record<AlertSeverity, { label: string; icon: string; color: string }> = {
  critical: { label: 'Critical', icon: '!', color: 'var(--status-critical)' },
  warning: { label: 'Warning', icon: '▲', color: 'var(--status-warning)' },
}

const STATE_LABEL: Record<AlertState, string> = {
  open: 'Open',
  acknowledged: 'Acknowledged',
  resolved: 'Resolved',
}

/** Severity as color + icon + label, never color alone. */
export function SeverityBadge({ severity }: { severity: AlertSeverity }) {
  const { label, icon, color } = SEVERITY[severity]
  return (
    <span className="badge">
      <span className="dot" style={{ background: color }} aria-hidden />
      <span className="icon" aria-hidden>{icon}</span>
      {label}
    </span>
  )
}

export function StateLabel({ alert }: { alert: Alert }) {
  return (
    <span className={`alert-state ${alert.state}`}>
      {STATE_LABEL[alert.state]}
      {alert.state === 'acknowledged' && alert.acknowledgedBy && <span className="muted"> · {alert.acknowledgedBy}</span>}
      {alert.escalatedAt && alert.state === 'open' && <span className="escalated"> · escalated</span>}
    </span>
  )
}

export function AcknowledgeButton({ alert, by }: { alert: Alert; by: string }) {
  const acknowledge = useAcknowledgeAlert()
  if (alert.state !== 'open') return null
  return (
    <span className="ack-action">
      <button
        type="button"
        className="small-button"
        // Disabled while in flight so a double click can't send two requests.
        disabled={acknowledge.isPending}
        onClick={(e) => {
          e.stopPropagation()
          acknowledge.mutate({ id: alert.id, by: by.trim() || 'operator' })
        }}
      >
        {acknowledge.isPending ? 'Acknowledging…' : acknowledge.isError ? 'Retry' : 'Acknowledge'}
      </button>
      {acknowledge.isError && <span className="inline-error" role="alert">{describeError(acknowledge.error).title}</span>}
    </span>
  )
}
