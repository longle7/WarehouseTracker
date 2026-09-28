import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    // The 3D view's chunk is ~940 kB because of three.js. It is lazy-loaded only when a
    // warehouse's 3D view opens, so it never blocks the initial page.
    chunkSizeWarningLimit: 1000,
  },
  server: {
    // Same-origin in dev: /api/* (REST and the SignalR hub) is forwarded to SensorDashboard.Api,
    // so no CORS setup.
    proxy: {
      '/api': {
        target: process.env.API_URL ?? 'http://localhost:5278',
        changeOrigin: true,
        ws: true, // SignalR WebSocket transport
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
