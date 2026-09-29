import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ApiError, isRetryable } from './api/client'
import { installGlobalErrorReporting } from './api/errorReporting'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import 'leaflet/dist/leaflet.css'
import './index.css'
import App from './App.tsx'
import { LiveProvider } from './api/LiveProvider'

/** Exponential backoff (1 s, 2 s, 4 s ... 15 s), or what the server asked for via Retry-After. */
const retryDelay = (attempt: number, error: unknown) =>
  error instanceof ApiError && error.retryAfter ? error.retryAfter * 1000 : Math.min(1000 * 2 ** attempt, 15_000)

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retry what can succeed later (timeouts, network, 5xx, 429), never a 4xx we caused.
      retry: (failures, error) => failures < 3 && isRetryable(error),
      retryDelay,
      staleTime: 2_000,
    },
    mutations: {
      retry: (failures, error) => failures < 2 && isRetryable(error),
      retryDelay,
    },
  },
})

installGlobalErrorReporting()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <LiveProvider>
        <App />
      </LiveProvider>
    </QueryClientProvider>
  </StrictMode>,
)
