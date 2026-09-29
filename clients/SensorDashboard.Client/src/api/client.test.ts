import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, isRetryable } from './client'
import { describeError } from './errorMessages'
import { reportError } from './errorReporting'

describe('isRetryable', () => {
  it('retries timeouts, network failures, server errors and rate limits', () => {
    expect(isRetryable(new ApiError('timeout', 'slow'))).toBe(true)
    expect(isRetryable(new ApiError('network', 'down'))).toBe(true)
    expect(isRetryable(new ApiError('http', 'boom', 500))).toBe(true)
    expect(isRetryable(new ApiError('http', 'busy', 503))).toBe(true)
    expect(isRetryable(new ApiError('http', 'slow down', 429))).toBe(true)
  })

  it('does not retry client errors or unknown failures', () => {
    expect(isRetryable(new ApiError('http', 'bad', 400))).toBe(false)
    expect(isRetryable(new ApiError('http', 'missing', 404))).toBe(false)
    expect(isRetryable(new Error('bug'))).toBe(false)
  })
})

describe('describeError', () => {
  it('explains each failure in terms the user can act on', () => {
    expect(describeError(new ApiError('timeout', 'x')).title).toMatch(/too long/)
    expect(describeError(new ApiError('network', 'x')).title).toMatch(/reach/)
    expect(describeError(new ApiError('http', 'x', 429, 3)).detail).toContain('3s')
    expect(describeError(new ApiError('http', 'Sensor not found', 404)).detail).toBe('Sensor not found')
  })
})

describe('reportError', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('sends a report once per message and caps the rate', () => {
    const fetch = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })))
    vi.stubGlobal('fetch', fetch)
    vi.stubGlobal('window', { location: { pathname: '/alerts' } })

    reportError(new Error('same problem'), 'test')
    reportError(new Error('same problem'), 'test')
    for (let i = 0; i < 20; i++) reportError(new Error(`problem ${i}`), 'test')

    expect(fetch).toHaveBeenCalledTimes(10)
    const body = JSON.parse((fetch.mock.calls[0] as unknown as [string, RequestInit])[1].body as string)
    expect(body).toMatchObject({ message: 'same problem', source: 'test', url: '/alerts' })
  })
})
