import { Link, useNavigate, useParams } from 'react-router'
import { useLiveSubscription } from '../api/live'
import { useSensors, useWarehouses } from '../api/queries'
import type { Sensor } from '../api/types'
import { formatAgo, formatPercent, formatTemp } from '../components/format'
import { QueryState } from '../components/QueryState'
import { StatusBadge } from '../components/StatusBadge'
import { Stat } from '../components/Stat'

export function WarehousePage() {
  const { warehouseId = '' } = useParams()
  const sensorsQuery = useSensors(warehouseId)
  useLiveSubscription('warehouse', warehouseId)
  // Shares the cached, polled warehouse list with the overview page.
  const warehouse = useWarehouses().data?.find((w) => w.id === warehouseId)

  return (
    <main className="page">
      <nav className="breadcrumbs">
        <Link to="/">Warehouses</Link>
        <span>{warehouse?.name ?? warehouseId}</span>
      </nav>
      <div className="page-title">
        <h1>{warehouse?.name ?? warehouseId}</h1>
        {warehouse && <p className="subtitle">{warehouse.city}</p>}
      </div>
      <QueryState query={sensorsQuery}>{(sensors) => <SensorTable warehouseId={warehouseId} sensors={sensors} />}</QueryState>
    </main>
  )
}

function SensorTable({ warehouseId, sensors }: { warehouseId: string; sensors: Sensor[] }) {
  const navigate = useNavigate()
  const count = (status: Sensor['status']) => sensors.filter((s) => s.status === status).length

  return (
    <>
      <div className="stats">
        <Stat label="Sensors" value={sensors.length} />
        <Stat label="OK" value={count('ok')} />
        <Stat label="Alerts" value={count('alert')} />
        <Stat label="Offline" value={count('offline')} />
      </div>
      <section className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Location</th>
                <th>Sensor</th>
                <th>Status</th>
                <th className="num">Temperature</th>
                <th className="num">Safe range</th>
                <th className="num">Humidity</th>
                <th>Door</th>
                <th>Last reading</th>
              </tr>
            </thead>
            <tbody>
              {sensors.map((s) => {
                const r = s.lastReading
                const outOfRange = r && (r.temperature < s.minTemperatureF || r.temperature > s.maxTemperatureF)
                const href = `/warehouses/${warehouseId}/sensors/${s.id}`
                return (
                  <tr key={s.id} className="clickable" onClick={() => navigate(href)}>
                    <td>
                      <Link to={href} onClick={(e) => e.stopPropagation()}>{s.location}</Link>
                    </td>
                    <td className="muted">{s.id}</td>
                    <td><StatusBadge status={s.status} /></td>
                    <td className={`num ${outOfRange ? 'out-of-range' : ''}`}>{r ? formatTemp(r.temperature) : '—'}</td>
                    <td className="num muted">{s.minTemperatureF}–{s.maxTemperatureF}°F</td>
                    <td className="num">{r ? formatPercent(r.humidity, 1) : '—'}</td>
                    <td>{r ? (r.doorOpen ? 'Open' : 'Closed') : '—'}</td>
                    <td className="muted">{r ? formatAgo(r.timestamp) : 'No data (24h)'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </section>
    </>
  )
}
