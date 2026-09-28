import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { BASE_URL } from './client'
import { LiveContext, type LiveState, type LiveTopic } from './live'
import { queryKeys } from './queries'
import type { Sensor, Warehouse } from './types'

const HUB_URL = `${BASE_URL}/hubs/telemetry`
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000]

const hubMethod = (action: 'Subscribe' | 'Unsubscribe', topic: LiveTopic) =>
  `${action}${topic === 'warehouse' ? 'Warehouse' : 'Sensor'}`

/**
 * Owns the SignalR connection and writes pushed data straight into the React Query cache,
 * so pages read the same queries whether data arrived by push or by fetch.
 */
export function LiveProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')
  const connectionRef = useRef<HubConnection | null>(null)
  // "topic|id" -> number of mounted subscribers
  const subscriptions = useRef(new Map<string, number>())

  const invoke = useCallback((action: 'Subscribe' | 'Unsubscribe', topic: LiveTopic, id: string) => {
    const connection = connectionRef.current
    if (connection?.state !== HubConnectionState.Connected) return // re-sent on (re)connect
    connection.invoke(hubMethod(action, topic), id).catch(() => {})
  }, [])

  useEffect(() => {
    let disposed = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect(RETRY_DELAYS_MS)
      .configureLogging(LogLevel.Warning)
      .build()
    connectionRef.current = connection

    // Handlers must return undefined: SignalR treats a returned value as a client result
    // and logs an error because the server didn't ask for one.
    connection.on('warehousesUpdated', (warehouses: Warehouse[]) => {
      queryClient.setQueryData(queryKeys.warehouses, warehouses)
    })
    connection.on('sensorsUpdated', (warehouseId: string, sensors: Sensor[]) => {
      queryClient.setQueryData(queryKeys.sensors(warehouseId), sensors)
    })
    // History is bucketed server-side, so refetch it rather than appending raw readings.
    connection.on('readingsIngested', (sensorId: string) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.sensorPropertiesAll(sensorId) })
    })

    const resubscribe = () => {
      for (const key of subscriptions.current.keys()) {
        const [topic, id] = key.split('|') as [LiveTopic, string]
        invoke('Subscribe', topic, id)
      }
    }

    const onConnected = (resync: boolean) => {
      setState('live')
      resubscribe()
      // After a drop, catch up on anything pushed while disconnected.
      if (resync) queryClient.invalidateQueries()
    }

    // withAutomaticReconnect only covers drops after a successful start; retry the
    // initial start (and restarts after it gives up) here.
    const start = async (attempt = 0) => {
      try {
        await connection.start()
        if (!disposed) onConnected(attempt > 0)
      } catch {
        if (disposed) return
        setState('offline')
        retryTimer = setTimeout(() => start(attempt + 1), RETRY_DELAYS_MS[Math.min(attempt + 1, RETRY_DELAYS_MS.length - 1)])
      }
    }

    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => onConnected(true))
    connection.onclose(() => {
      if (disposed) return
      setState('offline')
      retryTimer = setTimeout(() => start(1), RETRY_DELAYS_MS[RETRY_DELAYS_MS.length - 1])
    })

    // Deferred a tick so React StrictMode's dev-only mount/unmount/mount cancels the timer
    // instead of aborting a real negotiation (which SignalR logs as an error).
    retryTimer = setTimeout(() => start(), 0)

    return () => {
      disposed = true
      clearTimeout(retryTimer)
      connectionRef.current = null
      connection.stop().catch(() => {})
    }
  }, [queryClient, invoke])

  const subscribe = useCallback((topic: LiveTopic, id: string) => {
    const key = `${topic}|${id}`
    const count = subscriptions.current.get(key) ?? 0
    subscriptions.current.set(key, count + 1)
    if (count === 0) invoke('Subscribe', topic, id)

    return () => {
      const remaining = (subscriptions.current.get(key) ?? 1) - 1
      if (remaining > 0) {
        subscriptions.current.set(key, remaining)
      } else {
        subscriptions.current.delete(key)
        invoke('Unsubscribe', topic, id)
      }
    }
  }, [invoke])

  const value = useMemo(() => ({ state, subscribe }), [state, subscribe])
  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>
}
