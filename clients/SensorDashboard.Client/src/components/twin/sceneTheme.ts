import { useEffect, useState } from 'react'
import type { SensorStatus } from '../../api/types'

// three.js needs real colors, not CSS variables. Status colors are fixed across themes
// (same values as --status-* in index.css); surface colors are read from CSS at runtime.
export const STATUS_HEX: Record<SensorStatus, string> = {
  ok: '#0ca30c',
  alert: '#d03b3b',
  offline: '#fab219',
  inactive: '#898781',
}

export interface SceneTheme {
  floor: string
  grid: string
  wall: string
  unit: string
  unitMuted: string
  rack: string
  accent: string
}

const VARS: Record<keyof SceneTheme, string> = {
  floor: '--twin-floor',
  grid: '--twin-grid',
  wall: '--twin-wall',
  unit: '--twin-unit',
  unitMuted: '--twin-unit-muted',
  rack: '--twin-rack',
  accent: '--accent',
}

function readTheme(): SceneTheme {
  const style = getComputedStyle(document.documentElement)
  return Object.fromEntries(
    Object.entries(VARS).map(([key, cssVar]) => [key, style.getPropertyValue(cssVar).trim() || '#888888']),
  ) as unknown as SceneTheme
}

/** Scene colors from the page theme, re-read when the OS light/dark setting changes. */
export function useSceneTheme(): SceneTheme {
  const [theme, setTheme] = useState(readTheme)
  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const update = () => setTheme(readTheme())
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  return theme
}
