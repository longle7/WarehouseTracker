import { Link } from 'react-router'
import { Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, YAxis } from 'recharts'
import { useLiveSubscription } from '../../api/live'
import { useSensorProperties } from '../../api/queries'
import type { SceneUnit, Sensor, UnitType } from '../../api/types'
import { formatAgo, formatPercent, formatTemp, formatTime } from '../format'
import { StatusBadge } from '../StatusBadge'

const UNIT_LABEL: Record<UnitType, string> = {
  walkInCooler: 'Walk-in cooler',
  reachInFridge: 'Reach-in fridge',
  displayCase: 'Display case',
}

interface Props {
  warehouseId: string
  units: SceneUnit[]
  sensors: Sensor[]
  selectedId: string | null
  onSelect: (sensorId: string | null) => void
}

/**
 * Side panel for the 3D view. With a unit selected it shows that fridge's live stats and a
 * 15-minute sparkline; otherwise a list of units, which doubles as the keyboard-accessible
 * way to select one (the canvas itself is pointer-only).
 */
export function UnitDetailPanel({ warehouseId, units, sensors, selectedId, onSelect }: Props) {
  const unit = units.find((u) => u.sensorId === selectedId)
  const sensor = sensors.find((s) => s.id === selectedId)

  if (!unit) {
    return (
      <aside className="card twin-panel">
        <h2>Units</h2>
        <p className="muted">Click a fridge in the 3D view, or pick one here.</p>
        <ul className="unit-list">
          {units.map((u) => {
            const s = sensors.find((x) => x.id === u.sensorId)
            return (
              <li key={u.sensorId}>
                <button type="button" onClick={() => onSelect(u.sensorId)}>
                  <span>{u.location}</span>
                  {s && <StatusBadge status={s.status} />}
                </button>
              </li>
            )
          })}
        </ul>
      </aside>
    )
  }

  const r = sensor?.lastReading
  const outOfRange = sensor && r && (r.temperature < sensor.minTemperatureF || r.temperature > sensor.maxTemperatureF)

  return (
    <aside className="card twin-panel">
      <div className="card-title">
        <div>
          <h2>{unit.location}</h2>
          <div className="muted small">{UNIT_LABEL[unit.unitType]} · {unit.sensorId}</div>
        </div>
        <button type="button" className="icon-button" aria-label="Close details" onClick={() => onSelect(null)}>×</button>
      </div>
      {sensor && <StatusBadge status={sensor.status} />}

      <dl className="unit-stats">
        <div>
          <dt>Temperature</dt>
          <dd className={outOfRange ? 'out-of-range' : undefined}>{r ? formatTemp(r.temperature) : '—'}</dd>
        </div>
        <div>
          <dt>Safe range</dt>
          <dd>{sensor ? `${sensor.minTemperatureF}–${sensor.maxTemperatureF}°F` : '—'}</dd>
        </div>
        <div>
          <dt>Humidity</dt>
          <dd>{r ? formatPercent(r.humidity, 1) : '—'}</dd>
        </div>
        <div>
          <dt>Door</dt>
          <dd>{r ? (r.doorOpen ? 'Open' : 'Closed') : '—'}</dd>
        </div>
        <div>
          <dt>Last reading</dt>
          <dd>{r ? formatAgo(r.timestamp) : 'No data (24h)'}</dd>
        </div>
      </dl>

      {sensor && <Sparkline sensor={sensor} />}

      <Link className="button-link" to={`/warehouses/${warehouseId}/sensors/${unit.sensorId}`}>
        Open full history →
      </Link>
    </aside>
  )
}

function Sparkline({ sensor }: { sensor: Sensor }) {
  useLiveSubscription('sensor', sensor.id)
  const { data } = useSensorProperties(sensor.id, 15)
  const points = (data?.find((p) => p.name === 'temperature')?.values ?? []).map((v) => ({
    t: new Date(v.timestamp).getTime(),
    value: v.value,
  }))

  return (
    <figure className="sparkline">
      <figcaption>Temperature, last 15 minutes</figcaption>
      {points.length === 0 ? (
        <div className="muted small">No readings yet.</div>
      ) : (
        <div className="chart tiny">
          <ResponsiveContainer>
            <LineChart data={points} margin={{ top: 6, right: 4, bottom: 4, left: 4 }}>
              <YAxis hide domain={[(min: number) => Math.min(min, sensor.minTemperatureF) - 1, (max: number) => Math.max(max, sensor.maxTemperatureF) + 1]} />
              <ReferenceLine y={sensor.maxTemperatureF} stroke="var(--text-muted)" strokeDasharray="3 3" />
              <ReferenceLine y={sensor.minTemperatureF} stroke="var(--text-muted)" strokeDasharray="3 3" />
              <Line dataKey="value" stroke="var(--series-1)" strokeWidth={2} dot={false} isAnimationActive={false} />
              <Tooltip
                isAnimationActive={false}
                cursor={{ stroke: 'var(--axis)' }}
                content={({ active, payload }) => {
                  const p = payload?.[0]?.payload as { t: number; value: number } | undefined
                  return active && p ? (
                    <div className="chart-tooltip">
                      <div className="time">{formatTime(p.t)}</div>
                      <strong>{formatTemp(p.value)}</strong>
                    </div>
                  ) : null
                }}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </figure>
  )
}
