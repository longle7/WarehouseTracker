import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import 'leaflet/dist/leaflet.css'
import './index.css'
import App from './App.tsx'
import { LiveProvider } from './api/LiveProvider'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Data is pushed or polled, so a short retry budget is enough; the next update recovers anyway.
      retry: 1,
      staleTime: 2_000,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <LiveProvider>
        <App />
      </LiveProvider>
    </QueryClientProvider>
  </StrictMode>,
)
