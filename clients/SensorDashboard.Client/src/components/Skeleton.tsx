/** Placeholder shapes shown while content loads, so the layout doesn't jump when it arrives. */

export function SkeletonBlock({ height = 16, width = '100%' }: { height?: number; width?: number | string }) {
  return <div className="skeleton" style={{ height, width }} aria-hidden />
}

export function SkeletonStats({ count = 4 }: { count?: number }) {
  return (
    <div className="stats" aria-busy="true" aria-label="Loading">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="card stat">
          <SkeletonBlock height={12} width="40%" />
          <SkeletonBlock height={28} width="55%" />
        </div>
      ))}
    </div>
  )
}

export function SkeletonList({ rows = 4, rowHeight = 56 }: { rows?: number; rowHeight?: number }) {
  return (
    <div className="card skeleton-list" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, i) => (
        <SkeletonBlock key={i} height={rowHeight} />
      ))}
    </div>
  )
}

export function SkeletonPanel({ height = 260 }: { height?: number }) {
  return (
    <div className="card" aria-busy="true" aria-label="Loading">
      <SkeletonBlock height={18} width="30%" />
      <div style={{ marginTop: 12 }}>
        <SkeletonBlock height={height} />
      </div>
    </div>
  )
}
