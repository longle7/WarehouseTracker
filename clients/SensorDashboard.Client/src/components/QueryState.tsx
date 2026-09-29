import type { UseQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { ApiError } from '../api/client'
import { describeError } from '../api/errorMessages'
import { formatAgo } from './format'
import { SkeletonBlock } from './Skeleton'

interface Props<T> {
  query: UseQueryResult<T>
  children: (data: T) => ReactNode
  /** Shown on first load; defaults to a generic skeleton. */
  loading?: ReactNode
  /** When this returns true for the data, `empty` is shown instead of `children`. */
  isEmpty?: (data: T) => boolean
  empty?: ReactNode
}

/**
 * Every async view goes through the same states: skeleton on first load, an error card with
 * retry when nothing could be loaded, an empty state, or the data. If a background refresh
 * fails, the last good data stays on screen under a "couldn't refresh" banner.
 */
export function QueryState<T>({ query, children, loading, isEmpty, empty }: Props<T>) {
  if (query.data === undefined) {
    if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
    return <>{loading ?? <SkeletonBlock height={160} />}</>
  }

  const content = isEmpty?.(query.data) && empty ? empty : children(query.data)
  return (
    <>
      {query.isError && <StaleBanner error={query.error} updatedAt={query.dataUpdatedAt} onRetry={() => void query.refetch()} />}
      {content}
    </>
  )
}

function ErrorState({ error, onRetry, retrying }: { error: unknown; onRetry: () => void; retrying: boolean }) {
  const { title, detail } = describeError(error)
  const traceId = error instanceof ApiError ? error.traceId : null
  return (
    <div className="card state error-state" role="alert">
      <h2>{title}</h2>
      <p className="muted">{detail}</p>
      <button type="button" className="small-button" onClick={onRetry} disabled={retrying}>
        {retrying ? 'Retrying…' : 'Try again'}
      </button>
      {traceId && <p className="muted small">Reference: {traceId}</p>}
    </div>
  )
}

function StaleBanner({ error, updatedAt, onRetry }: { error: unknown; updatedAt: number; onRetry: () => void }) {
  return (
    <div className="stale-banner" role="status">
      <span>
        Couldn't refresh ({describeError(error).title.toLowerCase()}). Showing data from{' '}
        {formatAgo(new Date(updatedAt).toISOString())}.
      </span>
      <button type="button" className="small-button" onClick={onRetry}>Retry</button>
    </div>
  )
}
