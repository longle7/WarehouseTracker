import { Link } from 'react-router'
import { useWarehouses } from '../api/queries'
import type { Warehouse } from '../api/types'
import { EmptyState } from '../components/EmptyState'
import { QueryState } from '../components/QueryState'
import { SkeletonList, SkeletonPanel, SkeletonStats } from '../components/Skeleton'
import { Stat } from '../components/Stat'
import { warehouseStatus } from '../components/status'
import { StatusBadge } from '../components/StatusBadge'
import { WarehouseMap } from '../components/WarehouseMap'

export function WarehousesPage() {
  const query = useWarehouses()

  return (
    <main className="page">
      <div className="page-title">
        <h1>Warehouses</h1>
        <p className="subtitle">Live cold-chain status across all sites. Select a pin or a warehouse to see its sensors.</p>
      </div>
      <QueryState
        query={query}
        loading={<><SkeletonStats /><div className="overview"><SkeletonPanel height={420} /><SkeletonList rows={3} /></div></>}
        isEmpty={(warehouses) => warehouses.length === 0}
        empty={
          <EmptyState icon="▦" title="No warehouses yet">
            Warehouses and their sensors are defined in the metadata database (seeded by the migrations).
          </EmptyState>
        }
      >
        {(warehouses) => <Overview warehouses={warehouses} />}
      </QueryState>
    </main>
  )
}

function Overview({ warehouses }: { warehouses: Warehouse[] }) {
  const total = (pick: (w: Warehouse) => number) => warehouses.reduce((sum, w) => sum + pick(w), 0)

  return (
    <>
      <div className="stats">
        <Stat label="Warehouses" value={warehouses.length} />
        <Stat label="Sensors" value={total((w) => w.sensorCount)} />
        <Stat label="Active alerts" value={total((w) => w.alertCount)} />
        <Stat label="Offline sensors" value={total((w) => w.offlineCount)} />
      </div>
      <div className="overview">
        <section className="card">
          <WarehouseMap warehouses={warehouses} />
        </section>
        <section className="card">
          <div className="card-title">
            <h2>All sites</h2>
          </div>
          <ul className="warehouse-list">
            {warehouses.map((w) => (
              <li key={w.id}>
                <Link className="warehouse-item" to={`/warehouses/${w.id}`}>
                  <div className="row">
                    <strong>{w.name}</strong>
                    <StatusBadge status={warehouseStatus(w)} />
                  </div>
                  <div className="row counts">
                    <span>{w.city}</span>
                    <span>
                      {w.sensorCount} sensors · {w.alertCount} alert{w.alertCount === 1 ? '' : 's'} · {w.offlineCount} offline
                    </span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </>
  )
}
