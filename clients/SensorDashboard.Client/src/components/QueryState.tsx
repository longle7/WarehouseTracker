import type { UseQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'

/**
 * Renders loading and error states for a query, and its children once data exists.
 * During background polls the last good data stays on screen.
 */
export function QueryState<T>({
  query,
  children,
}: {
  query: UseQueryResult<T>
  children: (data: T) => ReactNode
}) {
  if (query.data !== undefined) return <>{children(query.data)}</>
  if (query.isError) return <div className="state error">Couldn't load data: {query.error.message}</div>
  return <div className="state">Loading…</div>
}
