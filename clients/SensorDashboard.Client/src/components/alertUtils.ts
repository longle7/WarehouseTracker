import { useState } from 'react'
import type { AlertKind } from '../api/types'

export const ALERT_KIND_LABEL: Record<AlertKind, string> = {
  temperatureOutOfRange: 'Temperature out of range',
  doorOpenTooLong: 'Door left open',
  sensorOffline: 'Sensor offline',
}

const NAME_KEY = 'alerts.acknowledgeAs'

/** Who is acknowledging, remembered per browser (a convenience only; there is no auth yet). */
export function useAcknowledgeAs() {
  const [name, setName] = useState(() => {
    try {
      return localStorage.getItem(NAME_KEY) ?? ''
    } catch {
      return ''
    }
  })
  const update = (value: string) => {
    setName(value)
    try {
      localStorage.setItem(NAME_KEY, value)
    } catch {
      // Storage unavailable (private mode etc.): keep the in-memory value.
    }
  }
  return [name, update] as const
}

export function formatDuration(from: string, to: string | null, now = Date.now()) {
  const seconds = Math.max(0, Math.round(((to ? new Date(to).getTime() : now) - new Date(from).getTime()) / 1000))
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`
  const hours = Math.floor(minutes / 60)
  return hours < 24 ? `${hours}h ${minutes % 60}m` : `${Math.floor(hours / 24)}d ${hours % 24}h`
}
