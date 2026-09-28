import { createContext, useContext, useEffect } from 'react'

/**
 * - connecting: first connection attempt in progress
 * - live: SignalR connected; data arrives by push and polling is off
 * - reconnecting / offline: no push; queries fall back to polling
 */
export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

export type LiveTopic = 'warehouse' | 'sensor'

export interface LiveContextValue {
  state: LiveState
  /** Joins the hub group for a topic; returns the matching leave. Ref-counted across components. */
  subscribe: (topic: LiveTopic, id: string) => () => void
}

export const LiveContext = createContext<LiveContextValue>({ state: 'offline', subscribe: () => () => {} })

export const useLiveState = () => useContext(LiveContext).state

/** Receive pushed updates for a warehouse or sensor while the calling component is mounted. */
export function useLiveSubscription(topic: LiveTopic, id: string | undefined) {
  const { subscribe } = useContext(LiveContext)
  useEffect(() => (id ? subscribe(topic, id) : undefined), [subscribe, topic, id])
}
