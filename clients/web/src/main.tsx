import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { cereAutentificare, tokenCurent } from './api/autentificare'
import { App } from './App'
import './index.css'

const radacina = createRoot(document.getElementById('root')!)
const randeaza = () =>
  radacina.render(
    <StrictMode>
      <App />
    </StrictMode>,
  )

async function porneste() {
  // Paginile de autentificare nu au nevoie de sesiune.
  if (window.location.pathname.startsWith('/autentificare')) {
    randeaza()
    return
  }

  // Restul aplicației cere un token valabil; altfel, login (cu revenire la pagina cerută).
  if (await tokenCurent()) {
    randeaza()
  } else {
    await cereAutentificare()
  }
}

porneste().catch((e: Error) => {
  radacina.render(
    <div style={{ maxWidth: 560, margin: '15vh auto', fontFamily: 'Segoe UI, system-ui, sans-serif', color: '#1f2937' }}>
      <h2>Aplicația nu se poate conecta la server</h2>
      <p>{e.message}</p>
      <p>Verifică dacă API-ul rulează: „dotnet run --project src/Host/ErpConstructii.Api”.</p>
      <button onClick={() => location.reload()} style={{ padding: '8px 16px', cursor: 'pointer' }}>
        Reîncearcă
      </button>
    </div>,
  )
})
