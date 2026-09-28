import { Link, useSearchParams } from 'react-router'
import { useAlerts, useWarehouses } from '../api/queries'
import type { AlertFilter } from '../api/types'
import { AcknowledgeButton, SeverityBadge, StateLabel } from '../components/alerts'
import { ALERT_KIND_LABEL, formatDuration, useAcknowledgeAs } from '../components/alertUtils'
import { formatAgo, formatDateTime, formatTemp } from '../components/format'
import { QueryState } from '../components/QueryState'

const FILTERS: { value: AlertFilter; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'resolved', label: 'Resolved' },
  { value: 'all', label: 'All' },
]

export function AlertsPage() {
  const [params, setParams] = useSearchParams()
  const filter = (FILTERS.find((f) => f.value === params.get('state'))?.value ?? 'active') as AlertFilter
  const query = useAlerts(filter)
  const warehouses = new Map(useWarehouses().data?.map((w) => [w.id, w.name]))
  const [by, setBy] = useAcknowledgeAs()

  return (
    <main className="page">
      <div className="toolbar">
        <div className="page-title">
          <h1>Alerts</h1>
          <p className="subtitle">
            Raised when a condition lasts past its grace period; resolved automatically when readings return to normal.
          </p>
        </div>
        <div className="segmented" role="group" aria-label="Alert state">
          {FILTERS.map((f) => (
            <button
              key={f.value}
              type="button"
              aria-pressed={filter === f.value}
              onClick={() => setParams(f.value === 'active' ? {} : { state: f.value }, { replace: true })}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      <label className="ack-as">
        Acknowledge as
        <input type="text" value={by} placeholder="Your name" maxLength={100} onChange={(e) => setBy(e.target.value)} />
      </label>

      <QueryState query={query}>
        {(alerts) =>
          alerts.length === 0 ? (
            <div className="card state">{filter === 'active' ? 'No active alerts. All clear.' : 'No alerts.'}</div>
          ) : (
            <section className="card">
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Severity</th>
                      <th>Alert</th>
                      <th>Unit</th>
                      <th>State</th>
                      <th>Started</th>
                      <th className="num">Duration</th>
                      <th className="num">Peak</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {alerts.map((a) => (
                      <tr key={a.id}>
                        <td><SeverityBadge severity={a.severity} /></td>
                        <td>
                          <div>{ALERT_KIND_LABEL[a.kind]}</div>
                          <div className="muted small">{a.message}</div>
                        </td>
                        <td>
                          <Link to={`/warehouses/${a.warehouseId}?unit=${a.sensorId}`}>{a.location}</Link>
                          <div className="muted small">{warehouses.get(a.warehouseId) ?? a.warehouseId}</div>
                        </td>
                        <td><StateLabel alert={a} /></td>
                        <td title={formatDateTime(a.openedAt)}>{formatAgo(a.openedAt)}</td>
                        <td className="num">{formatDuration(a.openedAt, a.closedAt)}</td>
                        <td className="num">{a.peakTemperature != null ? formatTemp(a.peakTemperature) : '—'}</td>
                        <td><AcknowledgeButton alert={a} by={by} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )
        }
      </QueryState>
    </main>
  )
}
