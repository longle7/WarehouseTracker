import { lazy, Suspense } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useLiveSubscription } from '../api/live'
import { useSensors, useWarehouses, useWarehouseScene } from '../api/queries'
import type { Sensor } from '../api/types'
import { formatAgo, formatPercent, formatTemp } from '../components/format'
import { QueryState } from '../components/QueryState'
import { Stat } from '../components/Stat'
import { StatusBadge } from '../components/StatusBadge'
import { UnitDetailPanel } from '../components/twin/UnitDetailPanel'

// three.js is large; load it only when the 3D view is shown.
const WarehouseTwin = lazy(() => import('../components/twin/WarehouseTwin'))

type View = '3d' | 'table'

export function WarehousePage() {
  const { warehouseId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const view: View = params.get('view') === 'table' ? 'table' : '3d'
  const selectedId = params.get('unit')

  const sensorsQuery = useSensors(warehouseId)
  const sceneQuery = useWarehouseScene(warehouseId, view === '3d')
  useLiveSubscription('warehouse', warehouseId)
  // Shares the cached, live warehouse list with the overview page.
  const warehouse = useWarehouses().data?.find((w) => w.id === warehouseId)

  const update = (changes: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [key, value] of Object.entries(changes)) {
          if (value === null) next.delete(key)
          else next.set(key, value)
        }
        return next
      },
      { replace: true },
    )

  return (
    <main className="page">
      <nav className="breadcrumbs">
        <Link to="/">Warehouses</Link>
        <span>{warehouse?.name ?? warehouseId}</span>
      </nav>
      <div className="toolbar">
        <div className="page-title">
          <h1>{warehouse?.name ?? warehouseId}</h1>
          {warehouse && <p className="subtitle">{warehouse.city}</p>}
        </div>
        <div className="segmented" role="group" aria-label="View">
          <button type="button" aria-pressed={view === '3d'} onClick={() => update({ view: null })}>3D twin</button>
          <button type="button" aria-pressed={view === 'table'} onClick={() => update({ view: 'table', unit: null })}>Table</button>
        </div>
      </div>

      <QueryState query={sensorsQuery}>
        {(sensors) => (
          <>
            <SensorStats sensors={sensors} />
            {view === 'table' ? (
              <SensorTable warehouseId={warehouseId} sensors={sensors} />
            ) : (
              <QueryState query={sceneQuery}>
                {(scene) => (
                  <div className="twin-layout">
                    <section className="card twin-canvas" aria-label="3D view of the warehouse">
                      <Suspense fallback={<div className="state">Loading 3D view…</div>}>
                        <WarehouseTwin
                          scene={scene}
                          sensors={sensors}
                          selectedId={selectedId}
                          onSelect={(id) => update({ unit: id })}
                        />
                      </Suspense>
                      <p className="twin-hint">Drag to orbit · scroll to zoom · click a fridge for details</p>
                    </section>
                    <UnitDetailPanel
                      warehouseId={warehouseId}
                      units={scene.units}
                      sensors={sensors}
                      selectedId={selectedId}
                      onSelect={(id) => update({ unit: id })}
                    />
                  </div>
                )}
              </QueryState>
            )}
          </>
        )}
      </QueryState>
    </main>
  )
}

function SensorStats({ sensors }: { sensors: Sensor[] }) {
  const count = (status: Sensor['status']) => sensors.filter((s) => s.status === status).length
  return (
    <div className="stats">
      <Stat label="Sensors" value={sensors.length} />
      <Stat label="OK" value={count('ok')} />
      <Stat label="Alerts" value={count('alert')} />
      <Stat label="Offline" value={count('offline')} />
    </div>
  )
}

function SensorTable({ warehouseId, sensors }: { warehouseId: string; sensors: Sensor[] }) {
  const navigate = useNavigate()

  return (
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
  )
}
