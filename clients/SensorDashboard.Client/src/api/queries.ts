import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from './client'

// The simulator ticks every 5s; poll live views at the same rate. React Query pauses
// polling while the tab is hidden and dedupes requests shared between components.
const LIVE_REFETCH_MS = 5_000
const HISTORY_REFETCH_MS = 10_000

export const queryKeys = {
  warehouses: ['warehouses'] as const,
  sensors: (warehouseId: string) => ['warehouses', warehouseId, 'sensors'] as const,
  sensorProperties: (sensorId: string, windowMinutes: number) =>
    ['sensors', sensorId, 'properties', windowMinutes] as const,
}

export function useWarehouses() {
  return useQuery({
    queryKey: queryKeys.warehouses,
    queryFn: ({ signal }) => api.warehouses(signal),
    refetchInterval: LIVE_REFETCH_MS,
  })
}

export function useSensors(warehouseId: string) {
  return useQuery({
    queryKey: queryKeys.sensors(warehouseId),
    queryFn: ({ signal }) => api.sensors(warehouseId, signal),
    refetchInterval: LIVE_REFETCH_MS,
  })
}

/** Rolling window ending now; the window start is recomputed on every poll. */
export function useSensorProperties(sensorId: string, windowMinutes: number) {
  return useQuery({
    queryKey: queryKeys.sensorProperties(sensorId, windowMinutes),
    queryFn: ({ signal }) =>
      api.sensorProperties(sensorId, { from: new Date(Date.now() - windowMinutes * 60_000) }, signal),
    refetchInterval: HISTORY_REFETCH_MS,
    // Keep the old chart on screen while a new window loads instead of flashing a spinner.
    placeholderData: keepPreviousData,
  })
}
