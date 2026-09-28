import type { Sensor, SensorProperty, Warehouse } from './types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    // ASP.NET Core returns ProblemDetails; surface its title when there is one.
    const problem = await response.json().catch(() => null)
    throw new ApiError(response.status, problem?.title ?? `Request failed (${response.status})`)
  }
  return response.json() as Promise<T>
}

export const api = {
  warehouses: (signal?: AbortSignal) => getJson<Warehouse[]>('/warehouses', signal),

  sensors: (warehouseId: string, signal?: AbortSignal) =>
    getJson<Sensor[]>(`/warehouses/${encodeURIComponent(warehouseId)}/sensors`, signal),

  sensorProperties: (sensorId: string, range: { from: Date; to?: Date }, signal?: AbortSignal) => {
    const params = new URLSearchParams({ from: range.from.toISOString() })
    if (range.to) params.set('to', range.to.toISOString())
    return getJson<SensorProperty[]>(`/sensors/${encodeURIComponent(sensorId)}/properties?${params}`, signal)
  },
}
