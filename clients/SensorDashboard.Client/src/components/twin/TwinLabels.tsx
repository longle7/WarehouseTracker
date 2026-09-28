import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo } from 'react'
import { Vector3 } from 'three'
import type { SceneUnit, Sensor } from '../../api/types'
import { formatTemp } from '../format'
import { STATUS } from '../status'
import { STATUS_HEX } from './sceneTheme'

export type LabelRefs = RefObject<Map<string, HTMLElement>>

/**
 * Runs inside the Canvas: each frame, projects every unit's label anchor to screen space and
 * moves its DOM label directly (no React re-render). Labels behind the camera are hidden and
 * nearer ones stack on top.
 */
export function LabelTracker({ units, origin, labels }: { units: SceneUnit[]; origin: [number, number]; labels: LabelRefs }) {
  const point = useMemo(() => new Vector3(), [])
  useFrame(({ camera, size }) => {
    for (const unit of units) {
      const el = labels.current.get(unit.sensorId)
      if (!el) continue
      point.set(origin[0] + unit.x, unit.height + 0.6, origin[1] + unit.z).project(camera)
      const x = ((point.x + 1) / 2) * size.width
      const y = ((1 - point.y) / 2) * size.height
      el.style.transform = `translate(${x}px, ${y}px) translate(-50%, -100%)`
      el.style.visibility = point.z < 1 ? 'visible' : 'hidden'
      el.style.zIndex = String(Math.round((1 - point.z) * 10_000))
    }
  })
  return null
}

interface LabelLayerProps {
  units: SceneUnit[]
  sensorsById: Map<string, Sensor>
  selectedId: string | null
  hoveredId: string | null
  labels: LabelRefs
  onSelect: (sensorId: string) => void
  onHover: (sensorId: string | null) => void
}

/**
 * DOM labels overlaid on the canvas, in the normal React tree. Status is color + icon, never
 * color alone; the reading and status text appear on hover or selection to keep labels of
 * neighboring units from overlapping. Labels are buttons, so they also select their unit.
 */
export function LabelLayer({ units, sensorsById, selectedId, hoveredId, labels, onSelect, onHover }: LabelLayerProps) {
  return (
    <div className="twin-labels">
      {units.map((unit) => {
        const sensor = sensorsById.get(unit.sensorId)
        const status = sensor?.status ?? 'offline'
        const reading = sensor?.lastReading
        const expanded = unit.sensorId === selectedId || unit.sensorId === hoveredId
        return (
          <button
            key={unit.sensorId}
            type="button"
            ref={(el) => {
              if (el) labels.current.set(unit.sensorId, el)
              else labels.current.delete(unit.sensorId)
            }}
            className={`twin-label${unit.sensorId === selectedId ? ' selected' : ''}`}
            onClick={() => onSelect(unit.sensorId)}
            onPointerEnter={() => onHover(unit.sensorId)}
            onPointerLeave={() => onHover(null)}
            aria-label={`${unit.location}: ${STATUS[status].label}`}
          >
            <span className="dot" style={{ background: STATUS_HEX[status] }} />
            <span className="icon" aria-hidden>{STATUS[status].icon}</span>
            <span className="name">{unit.location}</span>
            {expanded && (
              <span className="value">
                {reading && status !== 'offline' ? `${formatTemp(reading.temperature)} · ` : ''}
                {STATUS[status].label}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
