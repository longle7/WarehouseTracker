import { api } from './client'

const MAX_REPORTS_PER_MINUTE = 10
const sentRecently = new Map<string, number>() // message -> last sent (ms)

/**
 * Reports an unexpected client-side error to the API's log. Throttled and de-duplicated so a
 * render loop or a failing interval can't flood the server: the same message at most once a
 * minute, and no more than ten reports a minute overall.
 */
export function reportError(error: unknown, source: string) {
  const err = error instanceof Error ? error : new Error(String(error))
  const now = Date.now()
  for (const [message, at] of sentRecently) if (now - at > 60_000) sentRecently.delete(message)
  if (sentRecently.has(err.message) || sentRecently.size >= MAX_REPORTS_PER_MINUTE) return
  sentRecently.set(err.message, now)

  api.reportClientError({
    message: err.message.slice(0, 500),
    stack: err.stack?.slice(0, 4000),
    source,
    url: window.location.pathname,
  })
}

/** Catches what React error boundaries can't: errors in event handlers, timers and promises. */
export function installGlobalErrorReporting() {
  window.addEventListener('error', (event) => reportError(event.error ?? event.message, 'window.onerror'))
  window.addEventListener('unhandledrejection', (event) => reportError(event.reason, 'unhandledrejection'))
}
