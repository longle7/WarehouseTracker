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

export type SensorPropertyName = 'temperature' | 'humidity' | 'doorOpen' | 'anomalies'

export interface PropertyValue {
  /** Start of the time bucket. */
  timestamp: string
  /** Bucket average; for doorOpen the fraction of readings with the door open; for anomalies a count. */
  value: number
  min: number | null
  max: number | null
}

export interface SensorProperty {
  name: SensorPropertyName
  unit: string
  values: PropertyValue[]
}
