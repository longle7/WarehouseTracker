import { describe, expect, it } from 'vitest'
import { type LabelBox, layoutLabels } from './labelLayout'

const box = (id: string, x: number, y: number, depth: number, width = 100, height = 20): LabelBox => ({ id, x, y, width, height, depth })

const overlaps = (a: { left: number; top: number }, b: { left: number; top: number }, w = 100, h = 20) =>
  a.left < b.left + w && a.left + w > b.left && a.top < b.top + h && a.top + h > b.top

describe('layoutLabels', () => {
  it('centers a lone label above its anchor', () => {
    expect(layoutLabels([box('a', 200, 100, 0.5)]).get('a')).toEqual({ left: 150, top: 80 })
  })

  it('leaves labels that do not collide where they are', () => {
    const layout = layoutLabels([box('a', 100, 100, 0.5), box('b', 400, 100, 0.6)])
    expect(layout.get('b')).toEqual({ left: 350, top: 80 })
  })

  it('keeps the nearer label in place and lifts the farther one above it', () => {
    const layout = layoutLabels([box('far', 130, 100, 0.9), box('near', 100, 100, 0.2)])
    const near = layout.get('near')!
    const far = layout.get('far')!

    expect(near).toEqual({ left: 50, top: 80 })
    expect(far.top).toBe(near.top - 20 - 4)
    expect(overlaps(near, far)).toBe(false)
  })

  it('stacks several colliding labels with no overlaps', () => {
    const layout = layoutLabels([box('a', 100, 100, 0.1), box('b', 110, 100, 0.2), box('c', 120, 100, 0.3), box('d', 90, 100, 0.4)])
    const positions = [...layout.values()]

    for (let i = 0; i < positions.length; i++)
      for (let j = i + 1; j < positions.length; j++) expect(overlaps(positions[i], positions[j])).toBe(false)
  })
})
