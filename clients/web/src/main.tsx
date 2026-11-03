import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { initializeazaSesiunea } from './api/client'
import { App } from './App'
import './index.css'

const radacina = createRoot(document.getElementById('root')!)

// Sesiunea demo trebuie cunoscută înainte de primele cereri (headerele X-Dev-*).
initializeazaSesiunea()
  .then(() =>
    radacina.render(
      <StrictMode>
        <App />
      </StrictMode>,
    ),
  )
  .catch((e: Error) => {
    radacina.render(
      <div style={{ maxWidth: 560, margin: '15vh auto', fontFamily: 'Segoe UI, system-ui, sans-serif', color: '#1f2937' }}>
        <h2>Aplicația nu se poate conecta la server</h2>
        <p>{e.message}</p>
        <button onClick={() => location.reload()} style={{ padding: '8px 16px', cursor: 'pointer' }}>
          Reîncearcă
        </button>
      </div>,
    )
  })
