import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from './client'
import { useLiveState } from './live'

// While SignalR is connected, LiveProvider pushes fresh data into these queries and polling
// is off. If the connection drops, they fall back to polling at the simulator's 5s tick.
// React Query pauses polling while the tab is hidden and dedupes shared requests.
const FALLBACK_REFETCH_MS = 5_000
const FALLBACK_HISTORY_REFETCH_MS = 10_000

export const queryKeys = {
  warehouses: ['warehouses'] as const,
  sensors: (warehouseId: string) => ['warehouses', warehouseId, 'sensors'] as const,
  sensorPropertiesAll: (sensorId: string) => ['sensors', sensorId, 'properties'] as const,
  sensorProperties: (sensorId: string, windowMinutes: number) =>
    ['sensors', sensorId, 'properties', windowMinutes] as const,
}

function usePollingFallback(intervalMs: number) {
  return useLiveState() === 'live' ? false : intervalMs
}

export function useWarehouses() {
  return useQuery({
    queryKey: queryKeys.warehouses,
    queryFn: ({ signal }) => api.warehouses(signal),
    refetchInterval: usePollingFallback(FALLBACK_REFETCH_MS),
  })
}

export function useSensors(warehouseId: string) {
  return useQuery({
    queryKey: queryKeys.sensors(warehouseId),
    queryFn: ({ signal }) => api.sensors(warehouseId, signal),
    refetchInterval: usePollingFallback(FALLBACK_REFETCH_MS),
  })
}

/** Rolling window ending now; the window start is recomputed on every fetch. */
export function useSensorProperties(sensorId: string, windowMinutes: number) {
  return useQuery({
    queryKey: queryKeys.sensorProperties(sensorId, windowMinutes),
    queryFn: ({ signal }) =>
      api.sensorProperties(sensorId, { from: new Date(Date.now() - windowMinutes * 60_000) }, signal),
    refetchInterval: usePollingFallback(FALLBACK_HISTORY_REFETCH_MS),
    // Keep the old chart on screen while a new window loads instead of flashing a spinner.
    placeholderData: keepPreviousData,
  })
}
