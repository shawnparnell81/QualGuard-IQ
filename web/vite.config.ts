import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// API origin for the dev proxy; UI tests point this at their own API instance.
const api = process.env.QGIQ_API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': api, '/health': api },
  },
  build: {
    rolldownOptions: {
      output: {
        // Long-lived vendor chunks so module pages stay small and cache well between releases.
        codeSplitting: {
          groups: [
            { name: 'vendor-charts', test: /node_modules[\\/](recharts|d3-|victory|decimal\.js|es-toolkit)/, priority: 30 },
            { name: 'vendor-mui', test: /node_modules[\\/](@mui|@emotion)/, priority: 20 },
            { name: 'vendor-react', test: /node_modules[\\/](react|react-dom|react-router|scheduler|@tanstack)/, priority: 10 },
            { name: 'vendor', test: /node_modules/, priority: 0 },
          ],
        },
      },
    },
  },
});
