import type {
  Alert,
  AlertFilter,
  Sensor,
  SensorHistory,
  SensorProperty,
  SystemStatus,
  Warehouse,
  WarehouseScene,
} from './types'

export const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

/** Requests that take longer than this are abandoned with a timeout error. */
export const REQUEST_TIMEOUT_MS = 10_000

export type ApiErrorKind = 'timeout' | 'network' | 'http'

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number
  /** Seconds the server asked us to wait (429/503), if it said. */
  readonly retryAfter: number | null
  /** Quote this when reporting a problem; it links to the server's logs. */
  readonly traceId: string | null

  constructor(kind: ApiErrorKind, message: string, status = 0, retryAfter: number | null = null, traceId: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.status = status
    this.retryAfter = retryAfter
    this.traceId = traceId
  }
}

/** Worth retrying: the same request may succeed later (not a 4xx the client caused). */
export function isRetryable(error: unknown): boolean {
  if (!(error instanceof ApiError)) return false
  return error.kind !== 'http' || error.status >= 500 || error.status === 429 || error.status === 408
}

async function request(path: string, signal?: AbortSignal, init?: RequestInit): Promise<Response> {
  // Abort on whichever comes first: the caller (e.g. React Query unmount) or our timeout.
  const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS)
  const combined = signal ? AbortSignal.any([signal, timeout]) : timeout

  let response: Response
  try {
    response = await fetch(`${BASE_URL}${path}`, {
      ...init,
      signal: combined,
      headers: { Accept: 'application/json', ...init?.headers },
    })
  } catch (error) {
    if (timeout.aborted) throw new ApiError('timeout', 'The server took too long to respond.')
    if (signal?.aborted) throw error // cancelled by the caller; not a failure
    throw new ApiError('network', "Can't reach the server. Check your connection.")
  }

  if (!response.ok) {
    // ASP.NET Core returns ProblemDetails; surface its title and trace ID when there is one.
    const problem = await response.json().catch(() => null)
    const retryAfter = Number(response.headers.get('Retry-After')) || null
    throw new ApiError('http', problem?.title ?? `Request failed (${response.status})`, response.status, retryAfter, problem?.traceId ?? null)
  }
  return response
}

async function getJson<T>(path: string, signal?: AbortSignal, init?: RequestInit): Promise<T> {
  return (await request(path, signal, init)).json() as Promise<T>
}

export const api = {
  warehouses: (signal?: AbortSignal) => getJson<Warehouse[]>('/warehouses', signal),

  sensors: (warehouseId: string, signal?: AbortSignal) =>
    getJson<Sensor[]>(`/warehouses/${encodeURIComponent(warehouseId)}/sensors`, signal),

  scene: (warehouseId: string, signal?: AbortSignal) =>
    getJson<WarehouseScene>(`/warehouses/${encodeURIComponent(warehouseId)}/scene`, signal),

  alerts: (filter: AlertFilter, warehouseId?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams({ state: filter })
    if (warehouseId) params.set('warehouseId', warehouseId)
    return getJson<Alert[]>(`/alerts?${params}`, signal)
  },

  acknowledgeAlert: (id: number, by: string) =>
    getJson<Alert>(`/alerts/${id}/acknowledge`, undefined, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ by }),
    }),

  status: (signal?: AbortSignal) => getJson<SystemStatus>('/status', signal),

  sensorProperties: async (sensorId: string, range: { from: Date; to?: Date }, signal?: AbortSignal): Promise<SensorHistory> => {
    const params = new URLSearchParams({ from: range.from.toISOString() })
    if (range.to) params.set('to', range.to.toISOString())
    const response = await request(`/sensors/${encodeURIComponent(sensorId)}/properties?${params}`, signal)
    return {
      properties: (await response.json()) as SensorProperty[],
      bucketSeconds: Number(response.headers.get('X-Bucket-Seconds') ?? 0),
      source: response.headers.get('X-History-Source') === 'rollup' ? 'rollup' : 'raw',
      lastReadingAt: response.headers.get('X-Last-Reading-At'),
    }
  },

  /** Fire-and-forget; reporting must never throw or retry. */
  reportClientError: (report: ClientErrorReport) => {
    void fetch(`${BASE_URL}/client-errors`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(report),
      keepalive: true,
      signal: AbortSignal.timeout(5_000),
    }).catch(() => {})
  },
}

export interface ClientErrorReport {
  message: string
  stack?: string
  source: string
  url: string
}
