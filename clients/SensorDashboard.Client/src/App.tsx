import { useLiveState } from './api/live'
import { createBrowserRouter, isRouteErrorResponse, Link, Outlet, RouterProvider, useRouteError } from 'react-router'

const LIVE_LABEL = {
  live: 'Live · pushed via SignalR',
  connecting: 'Connecting…',
  reconnecting: 'Reconnecting · polling every 5s',
  offline: 'Push unavailable · polling every 5s',
} as const

function Layout() {
  const live = useLiveState()

  return (
    <>
      <header className="app-header">
        <Link to="/" className="brand">Sensor Dashboard</Link>
        <span className={`live ${live}`} aria-live="polite">
          <span className="live-dot" aria-hidden />
          {LIVE_LABEL[live]}
        </span>
      </header>
      <Outlet />
    </>
  )
}

function RouteError() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error) ? `${error.status} ${error.statusText}` : 'Something went wrong.'
  return (
    <main className="page">
      <div className="state error">{message}</div>
      <Link to="/">Back to warehouses</Link>
    </main>
  )
}

const router = createBrowserRouter([
  {
    element: <Layout />,
    errorElement: <RouteError />,
    children: [
      // Route-level code splitting: Leaflet and Recharts load only on the pages that use them.
      { path: '/', lazy: async () => ({ Component: (await import('./pages/WarehousesPage')).WarehousesPage }) },
      { path: '/warehouses/:warehouseId', lazy: async () => ({ Component: (await import('./pages/WarehousePage')).WarehousePage }) },
      {
        path: '/warehouses/:warehouseId/sensors/:sensorId',
        lazy: async () => ({ Component: (await import('./pages/SensorPage')).SensorPage }),
      },
    ],
  },
])

export default function App() {
  return <RouterProvider router={router} />
}
