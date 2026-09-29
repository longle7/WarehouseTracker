export interface LabelBox {
  id: string
  /** Anchor point in screen pixels: labels sit centered above it. */
  x: number
  y: number
  width: number
  height: number
  /** Projected depth; smaller is nearer the camera. */
  depth: number
}

export interface LabelPosition {
  /** Top-left corner in screen pixels. */
  left: number
  top: number
}

const GAP = 4
const MAX_NUDGES = 8

/**
 * Places labels above their anchors without overlapping. Nearer labels keep their natural
 * position; each farther label that would collide moves up just above what it hits, which
 * reads as a stack over neighboring units. Pure, so it runs every frame and is unit-tested.
 */
export function layoutLabels(boxes: LabelBox[]): Map<string, LabelPosition> {
  const placed: (LabelPosition & { width: number; height: number })[] = []
  const result = new Map<string, LabelPosition>()

  for (const box of [...boxes].sort((a, b) => a.depth - b.depth)) {
    const left = box.x - box.width / 2
    let top = box.y - box.height

    for (let i = 0; i < MAX_NUDGES; i++) {
      const hit = placed.find(
        (p) =>
          left < p.left + p.width + GAP &&
          left + box.width + GAP > p.left &&
          top < p.top + p.height + GAP &&
          top + box.height + GAP > p.top,
      )
      if (!hit) break
      top = hit.top - box.height - GAP
    }

    placed.push({ left, top, width: box.width, height: box.height })
    result.set(box.id, { left, top })
  }
  return result
}
