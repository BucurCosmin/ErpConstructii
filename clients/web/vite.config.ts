import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// În dezvoltare, cererile către API trec prin proxy: aceeași origine pentru aplicație, API și serverul
// de autentificare (cookie-ul de sesiune și redirecționările OAuth funcționează fără CORS).
const api = { target: 'http://localhost:5080', changeOrigin: false }

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/connect': api,
      '/.well-known': api,
      '/sanatate': api,
    },
  },
})
