// Mirrors IoTDigitalTwin.Contracts. The API serializes camelCase, enums as camelCase strings,
// and timestamps as ISO 8601 strings in UTC.

export type SensorStatus = 'ok' | 'alert' | 'offline' | 'inactive'

export interface Warehouse {
  id: string
  name: string
  city: string
  latitude: number
  longitude: number
  sensorCount: number
  /** Sensors whose latest reading is flagged or outside their temperature range. */
  alertCount: number
  /** Active sensors with no recent reading. */
  offlineCount: number
}

export interface LastReading {
  timestamp: string
  temperature: number
  humidity: number
  doorOpen: boolean
  isAnomaly: boolean
}

export interface Sensor {
  id: string
  warehouseId: string
  location: string
  minTemperatureF: number
  maxTemperatureF: number
  isActive: boolean
  status: SensorStatus
  lastReading: LastReading | null
}

export type UnitType = 'walkInCooler' | 'reachInFridge' | 'displayCase'

/** One refrigerated unit in a warehouse's 3D scene. Meters; rotation 0 = door faces +Z. */
export interface SceneUnit {
  sensorId: string
  location: string
  unitType: UnitType
  x: number
  z: number
  rotationDegrees: number
  width: number
  depth: number
  height: number
}

/** Static digital-twin layout. Live state is joined by sensorId from the sensor list. */
export interface WarehouseScene {
  warehouseId: string
  floorWidth: number
  floorDepth: number
  units: SceneUnit[]
}

export type SensorPropertyName = 'temperature' | 'humidity' | 'doorOpen' | 'anomalies'

export interface PropertyValue {
  /** Start of the time bucket. */
  timestamp: string
  /** Bucket average; for doorOpen the fraction of readings with the door open; for anomalies a count. */
  value: number
  /** Readings in the bucket. */
  count: number
  min: number | null
  max: number | null
}

export interface SensorProperty {
  name: SensorPropertyName
  unit: string
  values: PropertyValue[]
}

/** GET /sensors/{id}/properties plus the response headers needed to merge pushed readings. */
export interface SensorHistory {
  properties: SensorProperty[]
  bucketSeconds: number
  /** "raw" readings or 1-minute "rollup"s. */
  source: 'raw' | 'rollup'
  /** Newest reading already included (raw only). */
  lastReadingAt: string | null
}

/** A reading as pushed by the ReadingsIngested hub event (SensorReadingDto). */
export interface SensorReading {
  sensorId: string
  warehouseId: string
  timestamp: string
  temperature: number
  humidity: number
  doorOpen: boolean
  isAnomaly: boolean
}

export type AlertKind = 'temperatureOutOfRange' | 'doorOpenTooLong' | 'sensorOffline'
export type AlertSeverity = 'warning' | 'critical'
export type AlertState = 'open' | 'acknowledged' | 'resolved'
export type AlertFilter = 'active' | 'resolved' | 'all'

/** A debounced alert with a lifecycle (see AlertDto in the .NET contracts). */
export interface Alert {
  id: number
  sensorId: string
  warehouseId: string
  location: string
  kind: AlertKind
  severity: AlertSeverity
  state: AlertState
  /** When the condition began, not when the alert was raised. */
  openedAt: string
  lastSeenAt: string
  closedAt: string | null
  acknowledgedAt: string | null
  acknowledgedBy: string | null
  escalatedAt: string | null
  peakTemperature: number | null
  message: string
}

export type HealthState = 'healthy' | 'degraded' | 'unhealthy'

export interface HealthCheckResult {
  name: string
  status: HealthState
  description: string | null
  durationMs: number
}

export interface Incident {
  startedAt: string
  endedAt: string | null
  status: HealthState
  summary: string
}

/** GET /status: live health checks plus uptime history from the API's health sampler. */
export interface SystemStatus {
  status: HealthState
  checkedAt: string
  /** Share of samples that were healthy or degraded (i.e. up), 0-100; null before any samples. */
  uptime24h: number | null
  uptime7d: number | null
  checks: HealthCheckResult[]
  incidents: Incident[]
}
