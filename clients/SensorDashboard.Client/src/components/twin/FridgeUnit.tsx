import { type ThreeEvent, useFrame } from '@react-three/fiber'
import { useRef } from 'react'
import type { MeshStandardMaterial } from 'three'
import type { SceneUnit, Sensor } from '../../api/types'
import { STATUS_HEX, type SceneTheme } from './sceneTheme'

const DEG = Math.PI / 180

interface Props {
  unit: SceneUnit
  sensor: Sensor | undefined
  theme: SceneTheme
  selected: boolean
  hovered: boolean
  onSelect: (sensorId: string) => void
  onHover: (sensorId: string | null) => void
}

/**
 * One refrigerated unit, built from its dimensions. Live state drives the model: the status
 * beacon uses the status color (its DOM label adds icon + text), an alert makes the body
 * pulse, an open door swings open, and offline or inactive units are drawn faded.
 */
export function FridgeUnit({ unit, sensor, theme, selected, hovered, onSelect, onHover }: Props) {
  const bodyMaterial = useRef<MeshStandardMaterial>(null)

  const status = sensor?.status ?? 'offline'
  const doorOpen = sensor?.lastReading?.doorOpen ?? false
  const faded = status === 'offline' || status === 'inactive'
  const { width: w, depth: d, height: h } = unit

  // Alert pulse: modulate emissive intensity instead of re-rendering React.
  useFrame(({ clock }) => {
    const material = bodyMaterial.current
    if (!material) return
    material.emissiveIntensity = status === 'alert' ? 0.25 + 0.2 * Math.sin(clock.elapsedTime * 4) : hovered ? 0.12 : 0
  })

  const handleClick = (e: ThreeEvent<MouseEvent>) => {
    e.stopPropagation()
    onSelect(unit.sensorId)
  }
  const setHover = (value: boolean) => (e: ThreeEvent<PointerEvent>) => {
    e.stopPropagation()
    onHover(value ? unit.sensorId : null)
    document.body.style.cursor = value ? 'pointer' : ''
  }

  const isDisplayCase = unit.unitType === 'displayCase'
  // Walk-ins get a person-sized door on the left of the front face; reach-ins a full-front
  // door; display cases a glass lid hinged along the top-front edge.
  const door =
    unit.unitType === 'walkInCooler'
      ? { width: 1.2, height: 2.2, x: -w / 2 + 1 }
      : { width: w * 0.9, height: h * 0.9, x: 0 }

  return (
    <group
      position={[unit.x, 0, unit.z]}
      rotation={[0, unit.rotationDegrees * DEG, 0]}
      onClick={handleClick}
      onPointerOver={setHover(true)}
      onPointerOut={setHover(false)}
    >
      {/* Body */}
      <mesh position={[0, h / 2, 0]} castShadow receiveShadow>
        <boxGeometry args={[w, h, d]} />
        <meshStandardMaterial
          ref={bodyMaterial}
          color={faded ? theme.unitMuted : theme.unit}
          emissive={status === 'alert' ? STATUS_HEX.alert : theme.accent}
          emissiveIntensity={0}
          roughness={0.55}
          metalness={0.15}
          transparent={faded}
          opacity={faded ? 0.7 : 1}
        />
      </mesh>

      {/* Door / glass lid */}
      {isDisplayCase ? (
        <group position={[0, h, d / 2]} rotation={[doorOpen ? -0.6 : 0, 0, 0]}>
          <mesh position={[0, -0.25, 0.01]}>
            <boxGeometry args={[w * 0.95, 0.5, 0.04]} />
            <meshStandardMaterial color="#9fd3ff" transparent opacity={0.45} roughness={0.1} />
          </mesh>
        </group>
      ) : (
        // Hinge on the door's left edge; opening swings it outward.
        <group position={[door.x - door.width / 2, 0, d / 2]} rotation={[0, doorOpen ? -1.75 : 0, 0]}>
          <mesh position={[door.width / 2, door.height / 2, 0.03]} castShadow>
            <boxGeometry args={[door.width, door.height, 0.06]} />
            <meshStandardMaterial color={doorOpen ? STATUS_HEX.offline : theme.wall} roughness={0.4} />
          </mesh>
        </group>
      )}

      {/* Status beacon */}
      <mesh position={[w / 2 - 0.3, h + 0.15, d / 2 - 0.3]}>
        <cylinderGeometry args={[0.14, 0.14, 0.3, 20]} />
        <meshStandardMaterial color={STATUS_HEX[status]} emissive={STATUS_HEX[status]} emissiveIntensity={0.8} />
      </mesh>

      {/* Selection ring on the floor */}
      {(selected || hovered) && (
        <mesh position={[0, 0.02, 0]} rotation={[-Math.PI / 2, 0, 0]}>
          <ringGeometry args={[Math.max(w, d) * 0.62, Math.max(w, d) * 0.62 + (selected ? 0.25 : 0.12), 48]} />
          <meshBasicMaterial color={theme.accent} transparent opacity={selected ? 0.95 : 0.5} />
        </mesh>
      )}
    </group>
  )
}
