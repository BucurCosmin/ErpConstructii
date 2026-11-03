import { Button, Result, Spin } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { managerAutentificare } from '../../api/autentificare'

/** Revenirea de la serverul de autentificare: schimbă codul pe token-uri și deschide aplicația. */
export function Callback() {
  const [eroare, setEroare] = useState<string | null>(null)
  const procesat = useRef(false)

  useEffect(() => {
    // StrictMode rulează efectele de două ori în dezvoltare; codul de autorizare se poate folosi o singură dată.
    if (procesat.current) return
    procesat.current = true
    managerAutentificare
      .signinRedirectCallback()
      .then((user) => {
        const tinta = typeof user.url_state === 'string' && user.url_state.startsWith('/') ? user.url_state : '/'
        window.location.replace(tinta)
      })
      .catch((e: Error) => {
        const parametri = new URLSearchParams(window.location.search)
        setEroare(parametri.get('error_description') ?? e.message)
      })
  }, [])

  if (eroare) {
    return (
      <Result
        status="error"
        title="Autentificarea nu a reușit"
        subTitle={eroare}
        extra={
          <Button type="primary" onClick={() => managerAutentificare.signinRedirect()}>
            Încearcă din nou
          </Button>
        }
      />
    )
  }

  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
      <Spin size="large" description="Se deschide aplicația…" />
    </div>
  )
}
