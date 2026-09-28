# SensorDashboard.Client

React + TypeScript dashboard for the IoT digital twin. See the [root README](../../README.md) for the architecture and how to run the whole system.

```bash
npm install
npm run dev     # http://localhost:5173; /api and the SignalR hub are proxied to http://localhost:5278
npm run build   # type-check + production build
npm run lint
```

Set `API_URL` to proxy to a different API during development, or `VITE_API_BASE_URL` to call an API directly from a production build.

- `src/api/`: types mirroring the .NET contracts, fetch client, TanStack Query hooks, SignalR `LiveProvider`
- `src/pages/`: warehouse map, warehouse sensors, sensor history
- `src/components/`: map, charts, status badge, formatting
