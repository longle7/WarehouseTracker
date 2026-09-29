import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useLiveSubscription } from '../api/live'
import { useSensorProperties, useSensors, useWarehouse } from '../api/queries'
import { formatAgo, formatDoor, formatPercent, formatTemp } from '../components/format'
import { QueryState } from '../components/QueryState'
import { SkeletonPanel } from '../components/Skeleton'
import { SensorCharts } from '../components/SensorCharts'
import { StatusBadge } from '../components/StatusBadge'
import { Stat } from '../components/Stat'

const WINDOWS = [
  { label: '15m', minutes: 15 },
  { label: '1h', minutes: 60 },
  { label: '6h', minutes: 360 },
  { label: '24h', minutes: 1440 },
] as const

export function SensorPage() {
  const { warehouseId = '', sensorId = '' } = useParams()
  const [windowMinutes, setWindowMinutes] = useState<number>(60)

  // Sensor metadata and live status come from the (cached) sensor list for its warehouse.
  const sensorsQuery = useSensors(warehouseId)
  const sensor = sensorsQuery.data?.find((s) => s.id === sensorId)
  const warehouse = useWarehouse(warehouseId)
  const propertiesQuery = useSensorProperties(sensorId, windowMinutes)
  // Warehouse group keeps this sensor's status live; sensor group triggers chart refreshes.
  useLiveSubscription('warehouse', warehouseId)
  useLiveSubscription('sensor', sensorId)

  if (sensorsQuery.data && !sensor) {
    return <main className="page"><div className="state error">Sensor '{sensorId}' isn't in warehouse '{warehouseId}'.</div></main>
  }

  const r = sensor?.lastReading

  return (
    <main className="page">
      <nav className="breadcrumbs">
        <Link to="/">Warehouses</Link>
        <Link to={`/warehouses/${warehouseId}`}>{warehouse?.name ?? warehouseId}</Link>
        <span>{sensor?.location ?? sensorId}</span>
      </nav>

      <div className="toolbar">
        <div className="page-title">
          <h1>{sensor?.location ?? sensorId} {sensor && <StatusBadge status={sensor.status} />}</h1>
          <p className="subtitle">
            {sensorId}
            {sensor && ` · safe range ${sensor.minTemperatureF}–${sensor.maxTemperatureF}°F`}
          </p>
        </div>
        <div className="segmented" role="group" aria-label="Time window">
          {WINDOWS.map((w) => (
            <button key={w.minutes} type="button" aria-pressed={windowMinutes === w.minutes} onClick={() => setWindowMinutes(w.minutes)}>
              {w.label}
            </button>
          ))}
        </div>
      </div>

      <div className="stats">
        <Stat label="Temperature" value={r ? formatTemp(r.temperature) : '—'} />
        <Stat label="Humidity" value={r ? formatPercent(r.humidity, 1) : '—'} />
        <Stat label="Door" value={r ? formatDoor(r.doorOpen) : '—'} />
        <Stat label="Last reading" value={r ? formatAgo(r.timestamp) : '—'} />
      </div>

      {sensor && (
        <QueryState query={propertiesQuery} loading={<><SkeletonPanel /><SkeletonPanel height={180} /></>}>
          {(history) => <SensorCharts sensor={sensor} history={history} />}
        </QueryState>
      )}
    </main>
  )
}
