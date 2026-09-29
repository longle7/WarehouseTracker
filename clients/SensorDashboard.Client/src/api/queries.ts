import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import { useLiveState } from './live'
import type { AlertFilter } from './types'

// While SignalR is connected, LiveProvider pushes fresh data into these queries and polling
// is off. If the connection drops, they fall back to polling at the simulator's 5s tick.
// React Query pauses polling while the tab is hidden and dedupes shared requests.
const FALLBACK_REFETCH_MS = 5_000
const FALLBACK_HISTORY_REFETCH_MS = 10_000
// While live, pushed readings are merged into history incrementally; a periodic refetch
// reconciles server-side rounding and keeps rollup-backed windows (which don't merge) fresh.
const LIVE_HISTORY_RECONCILE_MS = 30_000

export const queryKeys = {
  warehouses: ['warehouses'] as const,
  sensors: (warehouseId: string) => ['warehouses', warehouseId, 'sensors'] as const,
  scene: (warehouseId: string) => ['warehouses', warehouseId, 'scene'] as const,
  alertsAll: ['alerts'] as const,
  alerts: (filter: AlertFilter, warehouseId?: string) => ['alerts', filter, warehouseId ?? '*'] as const,
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

/** Alerts; pushed changes (AlertsChanged) invalidate these, polling covers disconnects. */
export function useAlerts(filter: AlertFilter, warehouseId?: string) {
  return useQuery({
    queryKey: queryKeys.alerts(filter, warehouseId),
    queryFn: ({ signal }) => api.alerts(filter, warehouseId, signal),
    refetchInterval: usePollingFallback(FALLBACK_REFETCH_MS),
    placeholderData: keepPreviousData,
  })
}

export function useAcknowledgeAlert() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, by }: { id: number; by: string }) => api.acknowledgeAlert(id, by),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.alertsAll }),
  })
}

/** Static 3D layout: changes only with metadata, so fetch once and keep it. */
export function useWarehouseScene(warehouseId: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.scene(warehouseId),
    queryFn: ({ signal }) => api.scene(warehouseId, signal),
    staleTime: Infinity,
    enabled,
  })
}

/** Rolling window ending now; the window start is recomputed on every fetch. */
export function useSensorProperties(sensorId: string, windowMinutes: number) {
  const live = useLiveState() === 'live'
  return useQuery({
    queryKey: queryKeys.sensorProperties(sensorId, windowMinutes),
    queryFn: ({ signal }) =>
      api.sensorProperties(sensorId, { from: new Date(Date.now() - windowMinutes * 60_000) }, signal),
    refetchInterval: live ? LIVE_HISTORY_RECONCILE_MS : FALLBACK_HISTORY_REFETCH_MS,
    // Keep the old chart on screen while a new window loads instead of flashing a spinner.
    placeholderData: keepPreviousData,
  })
}
