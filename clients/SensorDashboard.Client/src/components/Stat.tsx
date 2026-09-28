export function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="card stat">
      <div className="label">{label}</div>
      <div className="value">{value}</div>
    </div>
  )
}
