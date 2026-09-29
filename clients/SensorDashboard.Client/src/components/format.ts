const timeFormat = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  month: 'short',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

export const formatTime = (value: string | number) => timeFormat.format(new Date(value))

export const formatDateTime = (value: string | number) => dateTimeFormat.format(new Date(value))

export function formatAgo(value: string, now = Date.now()) {
  const seconds = Math.max(0, Math.round((now - new Date(value).getTime()) / 1000))
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  return hours < 24 ? `${hours}h ago` : formatDateTime(value)
}

export const formatTemp = (value: number) => `${value.toFixed(1)}°F`

export const formatPercent = (value: number, digits = 0) => `${value.toFixed(digits)}%`

export const formatDoor = (doorOpen: boolean) => (doorOpen ? 'Open' : 'Closed')
