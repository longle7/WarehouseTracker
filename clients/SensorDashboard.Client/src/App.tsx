import { useLiveState } from './api/live'
import { useAlerts } from './api/queries'
import { createBrowserRouter, isRouteErrorResponse, Link, NavLink, Outlet, RouterProvider, useRouteError } from 'react-router'

const LIVE_LABEL = {
  live: 'Live · pushed via SignalR',
  connecting: 'Connecting…',
  reconnecting: 'Reconnecting · polling every 5s',
  offline: 'Push unavailable · polling every 5s',
} as const

function Layout() {
  const live = useLiveState()
  const activeAlerts = useAlerts('active').data ?? []
  const critical = activeAlerts.filter((a) => a.severity === 'critical' && a.state === 'open').length

  return (
    <>
      <header className="app-header">
        <Link to="/" className="brand">Sensor Dashboard</Link>
        <nav className="app-nav">
          <NavLink to="/" end>Warehouses</NavLink>
          <NavLink to="/alerts">
            Alerts
            {activeAlerts.length > 0 && (
              <span className={`count${critical > 0 ? ' critical' : ''}`} aria-label={`${activeAlerts.length} active`}>
                {activeAlerts.length}
              </span>
            )}
          </NavLink>
        </nav>
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
      { path: '/alerts', lazy: async () => ({ Component: (await import('./pages/AlertsPage')).AlertsPage }) },
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
