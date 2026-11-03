import { LockOutlined, MailOutlined, SafetyOutlined } from '@ant-design/icons'
import { Alert, Button, Form, Input, Typography } from 'antd'
import { useState } from 'react'
import { managerAutentificare, urlRevenireValid } from '../../api/autentificare'
import { apiCont } from '../../api/client'
import { CadruAutentificare } from './CadruAutentificare'

type Pas = 'parola' | 'mfa' | 'recuperare'

/**
 * Pagina de login. Creează sesiunea pe server (cookie), apoi revine la cererea de autorizare
 * OAuth (ReturnUrl), care emite codul pentru aplicație.
 */
export function Autentificare() {
  const [pas, setPas] = useState<Pas>('parola')
  const [eroare, setEroare] = useState<string | null>(null)
  const [seIncarca, setSeIncarca] = useState(false)
  const revenire = new URLSearchParams(window.location.search).get('ReturnUrl')

  const continua = async () => {
    if (urlRevenireValid(revenire)) window.location.assign(revenire)
    else await managerAutentificare.signinRedirect()
  }

  const trimite = async (cerere: () => Promise<{ stare: string }>) => {
    setEroare(null)
    setSeIncarca(true)
    try {
      const r = await cerere()
      if (r.stare === 'mfa_necesar') setPas('mfa')
      else await continua()
    } catch (e) {
      setEroare((e as Error).message)
    } finally {
      setSeIncarca(false)
    }
  }

  return (
    <CadruAutentificare>
      {pas === 'parola' ? (
        <>
          <Typography.Title level={3} style={{ marginBottom: 4 }}>
            Autentificare
          </Typography.Title>
          <Typography.Paragraph type="secondary">Intră în contul tău de lucru.</Typography.Paragraph>
          {eroare && <Alert type="error" showIcon title={eroare} style={{ marginBottom: 16 }} />}
          <Form
            layout="vertical"
            requiredMark={false}
            size="large"
            onFinish={(v: { email: string; parola: string }) => trimite(() => apiCont.post('/api/cont/autentificare', v))}
          >
            <Form.Item name="email" label="Email" rules={[{ required: true, type: 'email', message: 'Introdu adresa de email.' }]}>
              <Input prefix={<MailOutlined />} autoComplete="username" autoFocus />
            </Form.Item>
            <Form.Item name="parola" label="Parolă" rules={[{ required: true, message: 'Introdu parola.' }]}>
              <Input.Password prefix={<LockOutlined />} autoComplete="current-password" />
            </Form.Item>
            <Button type="primary" htmlType="submit" block loading={seIncarca}>
              Intră în cont
            </Button>
          </Form>
          <Typography.Paragraph type="secondary" style={{ marginTop: 24, fontSize: 13 }}>
            Ai uitat parola? Contactează administratorul firmei.
          </Typography.Paragraph>
        </>
      ) : (
        <>
          <SafetyOutlined style={{ fontSize: 36, color: '#1f5fbf' }} />
          <Typography.Title level={3} style={{ marginTop: 12, marginBottom: 4 }}>
            Verificare în doi pași
          </Typography.Title>
          <Typography.Paragraph type="secondary">
            {pas === 'mfa'
              ? 'Introdu codul de 6 cifre din aplicația de autentificare (Google Authenticator, Microsoft Authenticator etc.).'
              : 'Introdu unul dintre codurile de recuperare primite la activarea verificării în doi pași. Fiecare cod se poate folosi o singură dată.'}
          </Typography.Paragraph>
          {eroare && <Alert type="error" showIcon title={eroare} style={{ marginBottom: 16 }} />}
          <Form
            key={pas}
            layout="vertical"
            requiredMark={false}
            size="large"
            onFinish={(v: { cod: string }) =>
              trimite(() => apiCont.post('/api/cont/autentificare/mfa', pas === 'mfa' ? { cod: v.cod } : { codRecuperare: v.cod }))
            }
          >
            <Form.Item name="cod" rules={[{ required: true, message: 'Introdu codul.' }]}>
              {pas === 'mfa' ? (
                <Input.OTP length={6} autoFocus />
              ) : (
                <Input placeholder="xxxxx-xxxxx" autoFocus autoComplete="one-time-code" />
              )}
            </Form.Item>
            <Button type="primary" htmlType="submit" block loading={seIncarca}>
              Verifică
            </Button>
          </Form>
          <Button type="link" style={{ paddingInline: 0, marginTop: 12 }} onClick={() => setPas(pas === 'mfa' ? 'recuperare' : 'mfa')}>
            {pas === 'mfa' ? 'Nu ai acces la telefon? Folosește un cod de recuperare' : 'Folosește codul din aplicație'}
          </Button>
        </>
      )}
    </CadruAutentificare>
  )
}
