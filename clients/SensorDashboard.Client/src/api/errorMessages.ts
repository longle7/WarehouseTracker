import { ApiError } from './client'

/** A human title and detail for any failure, tuned to what the user can do about it. */
export function describeError(error: unknown): { title: string; detail: string } {
  if (error instanceof ApiError) {
    switch (error.kind) {
      case 'timeout':
        return { title: 'The server is taking too long', detail: 'It may be under heavy load. Try again in a moment.' }
      case 'network':
        return { title: "Can't reach the server", detail: 'Check your connection; data will load when it comes back.' }
      default:
        if (error.status === 429) return { title: 'Too many requests', detail: `Slow down a little; retry in ${error.retryAfter ?? 1}s.` }
        if (error.status >= 500) return { title: 'The server hit a problem', detail: error.message }
        return { title: "Couldn't load this", detail: error.message }
    }
  }
  return { title: 'Something went wrong', detail: error instanceof Error ? error.message : String(error) }
}
