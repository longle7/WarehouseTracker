import type { SensorStatus, Warehouse } from '../api/types'

export const STATUS: Record<SensorStatus, { label: string; icon: string; color: string }> = {
  ok: { label: 'OK', icon: '✓', color: 'var(--status-good)' },
  alert: { label: 'Alert', icon: '!', color: 'var(--status-critical)' },
  offline: { label: 'Offline', icon: '–', color: 'var(--status-warning)' },
  inactive: { label: 'Inactive', icon: '×', color: 'var(--status-inactive)' },
}

export function statusColor(status: SensorStatus) {
  return STATUS[status].color
}

/** Outside the sensor's safe range (bounds are safe), matching the API's status rules. */
export function isOutOfRange(sensor: { minTemperatureF: number; maxTemperatureF: number }, temperature: number) {
  return temperature < sensor.minTemperatureF || temperature > sensor.maxTemperatureF
}

/** Worst status across a warehouse's sensors: alert beats offline beats ok. */
export function warehouseStatus(warehouse: Warehouse): SensorStatus {
  if (warehouse.alertCount > 0) return 'alert'
  if (warehouse.offlineCount > 0) return 'offline'
  return 'ok'
}
