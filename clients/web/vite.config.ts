import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// În dezvoltare, cererile către API trec prin proxy (fără CORS).
const api = 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/dev': api,
      '/sanatate': api,
    },
  },
})
