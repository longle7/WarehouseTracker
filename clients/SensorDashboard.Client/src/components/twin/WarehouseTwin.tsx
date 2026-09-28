import { Grid, OrbitControls } from '@react-three/drei'
import { Canvas } from '@react-three/fiber'
import { useMemo, useRef, useState } from 'react'
import type { SceneUnit, Sensor, WarehouseScene } from '../../api/types'
import { FridgeUnit } from './FridgeUnit'
import { type SceneTheme, useSceneTheme } from './sceneTheme'
import { LabelLayer, LabelTracker } from './TwinLabels'

interface Props {
  scene: WarehouseScene
  sensors: Sensor[]
  selectedId: string | null
  onSelect: (sensorId: string | null) => void
}

const WALL_HEIGHT = 1.2 // cutaway walls so the interior stays visible
const WALL_THICKNESS = 0.25
const DOCK_DOOR_WIDTH = 5

/**
 * Procedural 3D digital twin of one warehouse (the TwinMaker scene, rebuilt with
 * react-three-fiber). Scene coordinates match the API: meters from the floor's corner, so the
 * whole scene is offset to center it on the orbit target.
 */
export default function WarehouseTwin({ scene, sensors, selectedId, onSelect }: Props) {
  const theme = useSceneTheme()
  const { floorWidth: fw, floorDepth: fd } = scene
  const span = Math.max(fw, fd)
  const sensorsById = useMemo(() => new Map(sensors.map((s) => [s.id, s])), [sensors])
  const racks = useMemo(() => layoutRacks(scene), [scene])
  const [hoveredId, setHoveredId] = useState<string | null>(null)
  const labels = useRef(new Map<string, HTMLElement>())

  return (
    <div className="twin-stage">
      <Canvas
        shadows="percentage"
        dpr={[1, 2]}
        // Three-quarter view from the dock side, far enough back to frame the whole floor.
        camera={{ position: [fw * 0.12, span * 0.72, fd * 0.5 + span * 0.5], fov: 45, near: 0.5, far: span * 6 }}
        onPointerMissed={() => onSelect(null)}
      >
        <ambientLight intensity={0.55} />
        <hemisphereLight args={['#ffffff', '#444444', 0.35]} />
        <directionalLight
          position={[fw * 0.4, span, fd * 0.6]}
          intensity={1.1}
          castShadow
          shadow-mapSize={[2048, 2048]}
          shadow-camera-left={-span}
          shadow-camera-right={span}
          shadow-camera-top={span}
          shadow-camera-bottom={-span}
        />

        <group position={[-fw / 2, 0, -fd / 2]}>
          <Floor width={fw} depth={fd} theme={theme} />
          <Walls width={fw} depth={fd} theme={theme} />
          {racks.map((r, i) => (
            <PalletRack key={i} x={r.x} z={r.z} theme={theme} />
          ))}
          {scene.units.map((unit) => (
            <FridgeUnit
              key={unit.sensorId}
              unit={unit}
              sensor={sensorsById.get(unit.sensorId)}
              theme={theme}
              selected={unit.sensorId === selectedId}
              hovered={unit.sensorId === hoveredId}
              onSelect={onSelect}
              onHover={setHoveredId}
            />
          ))}
        </group>
        <LabelTracker units={scene.units} origin={[-fw / 2, -fd / 2]} labels={labels} />

        <OrbitControls
          makeDefault
          enableDamping
          target={[0, 0, 0]}
          minDistance={span * 0.3}
          maxDistance={span * 2}
          maxPolarAngle={Math.PI / 2.15}
        />
      </Canvas>
      <LabelLayer
        units={scene.units}
        sensorsById={sensorsById}
        selectedId={selectedId}
        hoveredId={hoveredId}
        labels={labels}
        onSelect={onSelect}
        onHover={setHoveredId}
      />
    </div>
  )
}

function Floor({ width, depth, theme }: { width: number; depth: number; theme: SceneTheme }) {
  return (
    <group>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[width / 2, 0, depth / 2]} receiveShadow>
        <planeGeometry args={[width, depth]} />
        <meshStandardMaterial color={theme.floor} roughness={0.95} />
      </mesh>
      {/* 1 m cells with 5 m sections: a scale reference, like a TwinMaker scene's ground grid */}
      <Grid
        args={[width, depth]}
        position={[width / 2, 0.01, depth / 2]}
        cellSize={1}
        cellThickness={0.6}
        cellColor={theme.grid}
        sectionSize={5}
        sectionThickness={1}
        sectionColor={theme.grid}
        fadeDistance={Math.max(width, depth) * 3}
      />
    </group>
  )
}

function Walls({ width, depth, theme }: { width: number; depth: number; theme: SceneTheme }) {
  const side = (w: number) => (DOCK_DOOR_WIDTH < w ? (w - DOCK_DOOR_WIDTH) / 2 : w / 2)
  const segments: { x: number; z: number; w: number; d: number }[] = [
    { x: width / 2, z: 0, w: width, d: WALL_THICKNESS }, // back
    { x: 0, z: depth / 2, w: WALL_THICKNESS, d: depth }, // left
    { x: width, z: depth / 2, w: WALL_THICKNESS, d: depth }, // right
    // front wall, split around the loading-dock opening
    { x: side(width) / 2, z: depth, w: side(width), d: WALL_THICKNESS },
    { x: width - side(width) / 2, z: depth, w: side(width), d: WALL_THICKNESS },
  ]
  return (
    <group>
      {segments.map((s, i) => (
        <mesh key={i} position={[s.x, WALL_HEIGHT / 2, s.z]} castShadow receiveShadow>
          <boxGeometry args={[s.w, WALL_HEIGHT, s.d]} />
          <meshStandardMaterial color={theme.wall} roughness={0.8} />
        </mesh>
      ))}
    </group>
  )
}

const RACK = { width: 7, depth: 1.1, height: 3.6, levels: 3 }

function PalletRack({ x, z, theme }: { x: number; z: number; theme: SceneTheme }) {
  const posts = [-1, 1].flatMap((sx) => [-1, 1].map((sz) => [sx * (RACK.width / 2 - 0.05), sz * (RACK.depth / 2 - 0.05)]))
  return (
    <group position={[x, 0, z]}>
      {posts.map(([px, pz], i) => (
        <mesh key={i} position={[px, RACK.height / 2, pz]} castShadow>
          <boxGeometry args={[0.1, RACK.height, 0.1]} />
          <meshStandardMaterial color={theme.rack} />
        </mesh>
      ))}
      {Array.from({ length: RACK.levels }, (_, level) => (
        <mesh key={level} position={[0, 0.3 + level * 1.2, 0]} castShadow receiveShadow>
          <boxGeometry args={[RACK.width, 0.08, RACK.depth]} />
          <meshStandardMaterial color={theme.rack} />
        </mesh>
      ))}
    </group>
  )
}

/**
 * Decorative racks in the middle of the floor, in rows, skipping any spot that would
 * collide with a monitored unit (with walking clearance). Deterministic from the layout.
 */
function layoutRacks(scene: WarehouseScene) {
  const clearance = 1.5
  const blocked = (x: number, z: number) =>
    scene.units.some((u) => {
      const [hw, hd] = footprint(u)
      return (
        Math.abs(x - u.x) < RACK.width / 2 + hw + clearance && Math.abs(z - u.z) < RACK.depth / 2 + hd + clearance
      )
    })

  const racks: { x: number; z: number }[] = []
  for (let z = scene.floorDepth * 0.4; z <= scene.floorDepth * 0.72; z += 3.5) {
    for (let x = 3 + RACK.width / 2; x <= scene.floorWidth - 3 - RACK.width / 2; x += RACK.width + 3) {
      if (!blocked(x, z)) racks.push({ x, z })
    }
  }
  return racks
}

/** Half extents of a unit's footprint on the floor, accounting for rotation. */
function footprint(u: SceneUnit): [number, number] {
  const turned = Math.round(u.rotationDegrees / 90) % 2 !== 0
  return turned ? [u.depth / 2, u.width / 2] : [u.width / 2, u.depth / 2]
}
