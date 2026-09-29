import { describe, expect, it } from 'vitest'
import { canMerge, mergeReadings } from './mergeReadings'
import type { SensorHistory, SensorProperty, SensorReading } from './types'

const T0 = Date.parse('2026-09-28T12:00:00Z')
const iso = (ms: number) => new Date(ms).toISOString()

function reading(offsetSeconds: number, overrides: Partial<SensorReading> = {}): SensorReading {
  return {
    sensorId: 'S1',
    warehouseId: 'W1',
    timestamp: iso(T0 + offsetSeconds * 1000),
    temperature: 37,
    humidity: 50,
    doorOpen: false,
    isAnomaly: false,
    ...overrides,
  }
}

/** One 15 s bucket at T0 holding two readings: temps 36 and 38, one door-open, no anomalies. */
function history(overrides: Partial<SensorHistory> = {}): SensorHistory {
  const bucket = (value: number, min: number | null, max: number | null) => [{ timestamp: iso(T0), value, count: 2, min, max }]
  const properties: SensorProperty[] = [
    { name: 'temperature', unit: '°F', values: bucket(37, 36, 38) },
    { name: 'humidity', unit: '%', values: bucket(50, 49, 51) },
    { name: 'doorOpen', unit: 'ratio', values: bucket(0.5, null, null) },
    { name: 'anomalies', unit: 'count', values: bucket(0, null, null) },
  ]
  return { properties, bucketSeconds: 15, source: 'raw', lastReadingAt: iso(T0 + 5000), ...overrides }
}

const values = (h: SensorHistory, name: SensorProperty['name']) => h.properties.find((p) => p.name === name)!.values

describe('mergeReadings', () => {
  it('folds a reading into its existing bucket using the count', () => {
    const merged = mergeReadings(history(), [reading(10, { temperature: 40, doorOpen: true, isAnomaly: true })], T0 - 60_000)

    expect(values(merged, 'temperature')).toEqual([{ timestamp: iso(T0), value: 38, count: 3, min: 36, max: 40 }]) // (36+38+40)/3
    expect(values(merged, 'doorOpen')[0].value).toBeCloseTo(2 / 3)
    expect(values(merged, 'anomalies')[0].value).toBe(1)
    expect(merged.lastReadingAt).toBe(iso(T0 + 10_000))
  })

  it('starts a new bucket aligned to the bucket size', () => {
    const merged = mergeReadings(history(), [reading(17, { temperature: 41 })], T0 - 60_000)

    const temps = values(merged, 'temperature')
    expect(temps).toHaveLength(2)
    expect(temps[1]).toEqual({ timestamp: iso(T0 + 15_000), value: 41, count: 1, min: 41, max: 41 })
    expect(values(merged, 'doorOpen')[1]).toMatchObject({ value: 0, count: 1, min: null, max: null })
  })

  it('never counts a reading the fetched history already includes', () => {
    const cached = history()
    const merged = mergeReadings(cached, [reading(0), reading(5)], T0 - 60_000) // both at or before lastReadingAt

    expect(merged).toBe(cached)
  })

  it('drops buckets that slide out of the rolling window', () => {
    const merged = mergeReadings(history(), [reading(20)], T0 + 15_000)

    expect(values(merged, 'temperature').map((v) => v.timestamp)).toEqual([iso(T0 + 15_000)])
  })

  it('does not mutate the cached history', () => {
    const cached = history()
    const snapshot = JSON.stringify(cached)
    mergeReadings(cached, [reading(10, { temperature: 99 })], T0 - 60_000)

    expect(JSON.stringify(cached)).toBe(snapshot)
  })
})

describe('canMerge', () => {
  it('requires raw history with a known last reading and a bucket that divides a day', () => {
    expect(canMerge(history())).toBe(true)
    expect(canMerge(history({ source: 'rollup', lastReadingAt: null }))).toBe(false)
    expect(canMerge(history({ lastReadingAt: null }))).toBe(false)
    expect(canMerge(history({ bucketSeconds: 7 }))).toBe(false)
  })
})
