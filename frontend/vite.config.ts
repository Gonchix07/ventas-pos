import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // host: true bindea 0.0.0.0 (no solo localhost) — necesario para abrir el frontend desde otra
  // PC de la LAN (ej. una caja real) usando la IP del servidor en vez de "localhost".
  server: { host: true },
})
