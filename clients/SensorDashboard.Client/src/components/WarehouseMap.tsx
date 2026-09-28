import { CircleMarker, MapContainer, TileLayer, Tooltip } from 'react-leaflet'
import { useNavigate } from 'react-router'
import type { Warehouse } from '../api/types'
import { statusColor, warehouseStatus } from './status'

/**
 * One pin per warehouse, colored by its worst sensor status. CircleMarkers are SVG, which
 * avoids Leaflet's default image icons (they break under bundlers). Status is also shown as
 * text in the tooltip and in the list beside the map, so color never carries it alone.
 */
export function WarehouseMap({ warehouses }: { warehouses: Warehouse[] }) {
  const navigate = useNavigate()
  const bounds = warehouses.map((w) => [w.latitude, w.longitude] as [number, number])

  return (
    <MapContainer className="map" bounds={bounds} boundsOptions={{ padding: [60, 60] }} scrollWheelZoom={false}>
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {warehouses.map((w) => {
        const status = warehouseStatus(w)
        return (
          <CircleMarker
            key={w.id}
            center={[w.latitude, w.longitude]}
            radius={11}
            pathOptions={{ color: '#ffffff', weight: 2, fillColor: statusColor(status), fillOpacity: 1 }}
            eventHandlers={{ click: () => navigate(`/warehouses/${w.id}`) }}
          >
            <Tooltip direction="top" offset={[0, -10]} permanent={status !== 'ok'}>
              <strong>{w.name}</strong>
              <br />
              {w.alertCount > 0 && `${w.alertCount} alert${w.alertCount > 1 ? 's' : ''} `}
              {w.offlineCount > 0 && `${w.offlineCount} offline`}
              {status === 'ok' && `All ${w.sensorCount} sensors OK`}
            </Tooltip>
          </CircleMarker>
        )
      })}
    </MapContainer>
  )
}
