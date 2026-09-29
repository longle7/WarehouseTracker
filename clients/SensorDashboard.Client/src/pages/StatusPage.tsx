import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { HealthState } from '../api/types'
import { EmptyState } from '../components/EmptyState'
import { formatAgo, formatDateTime } from '../components/format'
import { QueryState } from '../components/QueryState'
import { SkeletonList, SkeletonStats } from '../components/Skeleton'
import { Stat } from '../components/Stat'

const HEALTH: Record<HealthState, { label: string; icon: string; color: string }> = {
  healthy: { label: 'Healthy', icon: '✓', color: 'var(--status-good)' },
  degraded: { label: 'Degraded', icon: '▲', color: 'var(--status-warning)' },
  unhealthy: { label: 'Down', icon: '!', color: 'var(--status-critical)' },
}

function HealthBadge({ status }: { status: HealthState }) {
  const { label, icon, color } = HEALTH[status]
  return (
    <span className="badge">
      <span className="dot" style={{ background: color }} aria-hidden />
      <span className="icon" aria-hidden>{icon}</span>
      {label}
    </span>
  )
}

const percent = (value: number | null) => (value === null ? '—' : `${value.toFixed(value === 100 ? 0 : 2)}%`)

function duration(from: string, to: string | null) {
  const seconds = Math.round(((to ? Date.parse(to) : Date.now()) - Date.parse(from)) / 1000)
  if (seconds < 90) return `${seconds}s`
  const minutes = Math.round(seconds / 60)
  return minutes < 90 ? `${minutes}m` : `${Math.round(minutes / 60)}h`
}

/** Uptime and health: what the API's own sampler has recorded, plus a live check. */
export function StatusPage() {
  const query = useQuery({
    queryKey: ['status'],
    queryFn: ({ signal }) => api.status(signal),
    refetchInterval: 15_000,
  })

  return (
    <main className="page">
      <div className="page-title">
        <h1>System status</h1>
        <p className="subtitle">Health checks run every 30 seconds; uptime counts healthy and degraded samples as up.</p>
      </div>
      <QueryState query={query} loading={<><SkeletonStats count={3} /><SkeletonList rows={4} rowHeight={40} /></>}>
        {(status) => (
          <>
            <div className="stats">
              <div className="card stat">
                <div className="label">Current status</div>
                <div className="value status-value"><HealthBadge status={status.status} /></div>
              </div>
              <Stat label="Uptime (24 hours)" value={percent(status.uptime24h)} />
              <Stat label="Uptime (7 days)" value={percent(status.uptime7d)} />
            </div>

            <section className="card">
              <div className="card-title">
                <h2>Checks</h2>
                <span className="muted small">Checked {formatAgo(status.checkedAt)}</span>
              </div>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>Check</th><th>Status</th><th>Detail</th><th className="num">Took</th></tr>
                  </thead>
                  <tbody>
                    {status.checks.map((c) => (
                      <tr key={c.name}>
                        <td>{c.name}</td>
                        <td><HealthBadge status={c.status} /></td>
                        <td className="muted">{c.description ?? '—'}</td>
                        <td className="num">{c.durationMs.toFixed(0)} ms</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            {status.incidents.length === 0 ? (
              <EmptyState icon="✓" title="No incidents">Nothing has gone wrong in the recorded history.</EmptyState>
            ) : (
              <section className="card">
                <div className="card-title"><h2>Recent incidents</h2></div>
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr><th>Started</th><th>Severity</th><th>What</th><th className="num">Lasted</th></tr>
                    </thead>
                    <tbody>
                      {status.incidents.map((i) => (
                        <tr key={i.startedAt}>
                          <td title={formatDateTime(i.startedAt)}>{formatAgo(i.startedAt)}</td>
                          <td><HealthBadge status={i.status} /></td>
                          <td className="incident-summary" title={i.summary}>{i.summary}</td>
                          <td className="num">{i.endedAt ? duration(i.startedAt, i.endedAt) : `ongoing (${duration(i.startedAt, null)})`}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            )}
          </>
        )}
      </QueryState>
    </main>
  )
}
