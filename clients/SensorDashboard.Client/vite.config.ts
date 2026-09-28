import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Same-origin in dev: /api/* is forwarded to SensorDashboard.Api, so no CORS setup.
    proxy: {
      '/api': {
        target: process.env.API_URL ?? 'http://localhost:5278',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
