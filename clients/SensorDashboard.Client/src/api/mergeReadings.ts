import type { PropertyValue, SensorHistory, SensorProperty, SensorReading } from './types'

const DAY_MS = 86_400_000

/**
 * True when pushed readings can be folded into this history exactly: it came from raw readings
 * (so the API told us the newest reading it includes) and its bucket divides a day, so bucket
 * boundaries computed from Unix time match SQL Server's DATE_BUCKET (midnight origin).
 */
export function canMerge(history: SensorHistory): boolean {
  const bucketMs = history.bucketSeconds * 1000
  return history.source === 'raw' && history.lastReadingAt !== null && bucketMs > 0 && DAY_MS % bucketMs === 0
}

/**
 * Folds pushed readings into a cached history, returning a new history (or the same object if
 * nothing changed). Readings at or before `lastReadingAt` are already included and skipped, so
 * a reading is never counted twice. Averages and ratios use each bucket's count, giving the
 * same numbers as a refetch (up to the server's display rounding). Buckets that slide out of
 * the rolling window are dropped.
 */
export function mergeReadings(
  history: SensorHistory,
  readings: SensorReading[],
  windowStartMs: number,
): SensorHistory {
  if (!canMerge(history)) return history

  const lastIncluded = Date.parse(history.lastReadingAt!)
  const fresh = readings
    .filter((r) => Date.parse(r.timestamp) > lastIncluded)
    .sort((a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp))
  if (fresh.length === 0) return history

  const bucketMs = history.bucketSeconds * 1000
  const properties = history.properties.map((property) => {
    // Work on a map from bucket start (ms) to value; copies keep the cached object untouched.
    const byStart = new Map<number, PropertyValue>(property.values.map((v) => [Date.parse(v.timestamp), { ...v }]))
    for (const reading of fresh) {
      const ts = Date.parse(reading.timestamp)
      const start = Math.floor(ts / bucketMs) * bucketMs
      byStart.set(start, fold(property.name, byStart.get(start), reading, start))
    }
    const windowStartBucket = Math.floor(windowStartMs / bucketMs) * bucketMs
    const values = [...byStart.entries()]
      .filter(([start]) => start >= windowStartBucket)
      .sort(([a], [b]) => a - b)
      .map(([, v]) => v)
    return { ...property, values } satisfies SensorProperty
  })

  return { ...history, properties, lastReadingAt: fresh[fresh.length - 1].timestamp }
}

function fold(name: SensorProperty['name'], current: PropertyValue | undefined, reading: SensorReading, start: number): PropertyValue {
  const sample = sampleOf(name, reading)
  if (!current) {
    const continuous = name === 'temperature' || name === 'humidity'
    return {
      timestamp: new Date(start).toISOString(),
      value: sample,
      count: 1,
      min: continuous ? sample : null,
      max: continuous ? sample : null,
    }
  }

  const count = current.count + 1
  return {
    ...current,
    count,
    // Anomalies are a per-bucket total; everything else is a mean over the bucket's readings.
    value: name === 'anomalies' ? current.value + sample : (current.value * current.count + sample) / count,
    min: current.min === null ? null : Math.min(current.min, sample),
    max: current.max === null ? null : Math.max(current.max, sample),
  }
}

function sampleOf(name: SensorProperty['name'], reading: SensorReading): number {
  switch (name) {
    case 'temperature':
      return reading.temperature
    case 'humidity':
      return reading.humidity
    case 'doorOpen':
      return reading.doorOpen ? 1 : 0
    case 'anomalies':
      return reading.isAnomaly ? 1 : 0
  }
}
