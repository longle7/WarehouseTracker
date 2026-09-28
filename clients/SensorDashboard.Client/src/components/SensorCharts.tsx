import {
  Area,
  Bar,
  BarChart,
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { ReactNode } from 'react'
import type { Sensor, SensorProperty, SensorPropertyName } from '../api/types'
import { formatDateTime, formatPercent, formatTemp, formatTime } from './format'

interface Row {
  t: number
  temp: number
  tempRange: [number, number]
  humidity: number
  humidityRange: [number, number]
  doorPct: number
  anomalies: number
}

/** Placeholder with no values; Recharts breaks lines and areas at nulls. */
interface GapRow {
  t: number
  gap: true
}

type ChartRow = Row | GapRow

const isGap = (row: ChartRow): row is GapRow => 'gap' in row

/**
 * The API omits empty buckets, so a sensor outage would otherwise be drawn as a straight
 * line between the readings either side of it. Insert a gap row wherever consecutive
 * buckets are more than two bucket-widths apart.
 */
function withGaps(rows: Row[]): ChartRow[] {
  if (rows.length < 2) return rows
  const bucketMs = Math.min(...rows.slice(1).map((r, i) => r.t - rows[i].t))
  return rows.flatMap((r, i): ChartRow[] =>
    i > 0 && r.t - rows[i - 1].t > bucketMs * 2 ? [{ t: rows[i - 1].t + bucketMs, gap: true }, r] : [r])
}

/** Pivot the property-oriented API response into one row per time bucket. */
function toRows(properties: SensorProperty[]): Row[] {
  const byName = Object.fromEntries(properties.map((p) => [p.name, p.values])) as Record<
    SensorPropertyName,
    SensorProperty['values'] | undefined
  >
  const temperature = byName.temperature ?? []
  const humidity = new Map(byName.humidity?.map((v) => [v.timestamp, v]))
  const door = new Map(byName.doorOpen?.map((v) => [v.timestamp, v.value]))
  const anomalies = new Map(byName.anomalies?.map((v) => [v.timestamp, v.value]))

  return temperature.map((v) => {
    const h = humidity.get(v.timestamp)
    return {
      t: new Date(v.timestamp).getTime(),
      temp: v.value,
      tempRange: [v.min ?? v.value, v.max ?? v.value],
      humidity: h?.value ?? 0,
      humidityRange: [h?.min ?? h?.value ?? 0, h?.max ?? h?.value ?? 0],
      doorPct: (door.get(v.timestamp) ?? 0) * 100,
      anomalies: anomalies.get(v.timestamp) ?? 0,
    }
  })
}

// Shared chrome: recessive grid and axes, text in muted ink.
const tick = { fill: 'var(--text-muted)', fontSize: 12 }
const margin = { top: 8, right: 12, bottom: 0, left: 0 }
const SYNC_ID = 'sensor-history'

function TimeAxis() {
  return (
    <XAxis
      dataKey="t"
      type="number"
      scale="time"
      domain={['dataMin', 'dataMax']}
      tickFormatter={formatTime}
      stroke="var(--axis)"
      tick={tick}
      minTickGap={48}
    />
  )
}

function ChartTooltip({ render }: { render: (row: Row) => ReactNode }) {
  return (
    <Tooltip
      cursor={{ stroke: 'var(--axis)', strokeWidth: 1 }}
      isAnimationActive={false}
      content={({ active, payload }) => {
        const row = payload?.[0]?.payload as ChartRow | undefined
        if (!active || !row || isGap(row)) return null
        return (
          <div className="chart-tooltip">
            <div className="time">{formatDateTime(row.t)}</div>
            {render(row)}
          </div>
        )
      }}
    />
  )
}

/** Marks buckets that contain an anomaly with a status-critical dot ringed in the surface color. */
function AnomalyDot(props: { cx?: number; cy?: number; index?: number; payload?: ChartRow }) {
  const { cx, cy, index, payload } = props
  if (!payload || isGap(payload) || !payload.anomalies || cx == null || cy == null) return <g key={`dot-${index}`} />
  return (
    <circle key={`dot-${index}`} cx={cx} cy={cy} r={5} fill="var(--status-critical)" stroke="var(--surface)" strokeWidth={2} />
  )
}

export function SensorCharts({ sensor, properties }: { sensor: Sensor; properties: SensorProperty[] }) {
  const rows = toRows(properties)
  if (rows.length === 0) return <div className="card state">No readings in this window.</div>
  const chartRows = withGaps(rows)

  const minT = sensor.minTemperatureF
  const maxT = sensor.maxTemperatureF

  return (
    <div className="chart-grid">
      <section className="card wide">
        <div className="card-title">
          <h2>Temperature</h2>
          <div className="chart-note">
            <span className="key"><span className="key-line" />Average</span>
            <span className="key"><span className="key-band" />Min–max in bucket</span>
            <span className="key"><span className="key-threshold" />Safe range</span>
            <span className="key"><span className="key-anomaly" />Anomaly</span>
          </div>
        </div>
        <div className="chart">
          <ResponsiveContainer>
            <ComposedChart data={chartRows} syncId={SYNC_ID} margin={margin}>
              <CartesianGrid stroke="var(--grid)" vertical={false} />
              {TimeAxis()}
              <YAxis
                stroke="var(--axis)"
                tick={tick}
                width={48}
                unit="°"
                domain={[
                  (dataMin: number) => Math.floor(Math.min(dataMin, minT) - 1),
                  (dataMax: number) => Math.ceil(Math.max(dataMax, maxT) + 1),
                ]}
              />
              <ReferenceLine y={maxT} stroke="var(--text-muted)" strokeDasharray="4 4"
                label={{ value: `Max ${maxT}°F`, position: 'insideTopRight', fill: 'var(--text-muted)', fontSize: 11 }} />
              <ReferenceLine y={minT} stroke="var(--text-muted)" strokeDasharray="4 4"
                label={{ value: `Min ${minT}°F`, position: 'insideBottomRight', fill: 'var(--text-muted)', fontSize: 11 }} />
              <Area dataKey="tempRange" stroke="none" fill="var(--series-1-band)" isAnimationActive={false} activeDot={false} />
              <Line dataKey="temp" stroke="var(--series-1)" strokeWidth={2} dot={AnomalyDot}
                activeDot={{ r: 5, stroke: 'var(--surface)', strokeWidth: 2 }} isAnimationActive={false} />
              <ChartTooltip
                render={(r) => (
                  <>
                    <div>Avg <strong>{formatTemp(r.temp)}</strong></div>
                    <div className="muted">Range {formatTemp(r.tempRange[0])} – {formatTemp(r.tempRange[1])}</div>
                    {r.anomalies > 0 && <div className="out-of-range">! {r.anomalies} anomalous reading{r.anomalies > 1 ? 's' : ''}</div>}
                  </>
                )}
              />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </section>

      <section className="card wide">
        <div className="card-title"><h2>Humidity</h2><span className="chart-note">Average, with min–max band</span></div>
        <div className="chart small">
          <ResponsiveContainer>
            <ComposedChart data={chartRows} syncId={SYNC_ID} margin={margin}>
              <CartesianGrid stroke="var(--grid)" vertical={false} />
              {TimeAxis()}
              <YAxis stroke="var(--axis)" tick={tick} width={48} unit="%" domain={['dataMin - 2', 'dataMax + 2']}
                tickFormatter={(v: number) => v.toFixed(0)} />
              <Area dataKey="humidityRange" stroke="none" fill="var(--series-1-band)" isAnimationActive={false} activeDot={false} />
              <Line dataKey="humidity" stroke="var(--series-1)" strokeWidth={2} dot={false}
                activeDot={{ r: 5, stroke: 'var(--surface)', strokeWidth: 2 }} isAnimationActive={false} />
              <ChartTooltip render={(r) => <div>Avg <strong>{formatPercent(r.humidity, 1)}</strong></div>} />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </section>

      <section className="card">
        <div className="card-title"><h2>Door open</h2><span className="chart-note">% of readings per bucket</span></div>
        <div className="chart small">
          <ResponsiveContainer>
            <BarChart data={chartRows} syncId={SYNC_ID} margin={margin} barCategoryGap={1}>
              <CartesianGrid stroke="var(--grid)" vertical={false} />
              <XAxis dataKey="t" tickFormatter={formatTime} stroke="var(--axis)" tick={tick} minTickGap={48} />
              <YAxis stroke="var(--axis)" tick={tick} width={48} unit="%" domain={[0, 100]} ticks={[0, 50, 100]} />
              <Bar dataKey="doorPct" fill="var(--series-1)" radius={[4, 4, 0, 0]} isAnimationActive={false} />
              <ChartTooltip render={(r) => <div>Door open <strong>{formatPercent(r.doorPct)}</strong></div>} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </section>

      <section className="card">
        <div className="card-title"><h2>Anomalies</h2><span className="chart-note">Flagged readings per bucket</span></div>
        <div className="chart small">
          <ResponsiveContainer>
            <BarChart data={chartRows} syncId={SYNC_ID} margin={margin} barCategoryGap={1}>
              <CartesianGrid stroke="var(--grid)" vertical={false} />
              <XAxis dataKey="t" tickFormatter={formatTime} stroke="var(--axis)" tick={tick} minTickGap={48} />
              <YAxis stroke="var(--axis)" tick={tick} width={48} allowDecimals={false} />
              <Bar dataKey="anomalies" fill="var(--status-critical)" radius={[4, 4, 0, 0]} isAnimationActive={false} />
              <ChartTooltip render={(r) => <div><strong>{r.anomalies}</strong> anomalous reading{r.anomalies === 1 ? '' : 's'}</div>} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </section>

      <details className="card wide">
        <summary>Show data table ({rows.length} buckets)</summary>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Bucket start</th>
                <th className="num">Avg temp</th>
                <th className="num">Min</th>
                <th className="num">Max</th>
                <th className="num">Humidity</th>
                <th className="num">Door open</th>
                <th className="num">Anomalies</th>
              </tr>
            </thead>
            <tbody>
              {[...rows].reverse().map((r) => (
                <tr key={r.t}>
                  <td>{formatDateTime(r.t)} {formatTime(r.t)}</td>
                  <td className={`num ${r.temp < minT || r.temp > maxT ? 'out-of-range' : ''}`}>{formatTemp(r.temp)}</td>
                  <td className="num">{formatTemp(r.tempRange[0])}</td>
                  <td className="num">{formatTemp(r.tempRange[1])}</td>
                  <td className="num">{formatPercent(r.humidity, 1)}</td>
                  <td className="num">{formatPercent(r.doorPct)}</td>
                  <td className="num">{r.anomalies}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  )
}
