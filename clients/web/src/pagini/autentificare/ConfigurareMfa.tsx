import { CopyOutlined, SafetyOutlined } from '@ant-design/icons'
import { Alert, App, Button, Flex, Form, Input, QRCode, Skeleton, Steps, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { managerAutentificare, urlRevenireValid } from '../../api/autentificare'
import { apiCont } from '../../api/client'
import { CadruAutentificare } from './CadruAutentificare'

interface Configurare {
  cheie: string
  uriOtp: string
  activat: boolean
}

/** Activarea verificării în doi pași (obligatorie pentru administratori și rolurile financiare). */
export function ConfigurareMfa() {
  const [configurare, setConfigurare] = useState<Configurare | null>(null)
  const [coduri, setCoduri] = useState<string[] | null>(null)
  const [eroare, setEroare] = useState<string | null>(null)
  const [seIncarca, setSeIncarca] = useState(false)
  const { message } = App.useApp()
  const revenire = new URLSearchParams(window.location.search).get('ReturnUrl')

  useEffect(() => {
    apiCont
      .get<Configurare>('/api/cont/mfa/configurare')
      .then(setConfigurare)
      .catch(() => window.location.assign(`/autentificare${window.location.search}`))
  }, [])

  const continua = async () => {
    if (urlRevenireValid(revenire)) window.location.assign(revenire)
    else await managerAutentificare.signinRedirect()
  }

  const activeaza = async ({ cod }: { cod: string }) => {
    setEroare(null)
    setSeIncarca(true)
    try {
      const r = await apiCont.post<{ coduriRecuperare: string[] }>('/api/cont/mfa/configurare', { cod })
      setCoduri(r.coduriRecuperare)
    } catch (e) {
      setEroare((e as Error).message)
    } finally {
      setSeIncarca(false)
    }
  }

  return (
    <CadruAutentificare>
      <SafetyOutlined style={{ fontSize: 36, color: '#1f5fbf' }} />
      <Typography.Title level={3} style={{ marginTop: 12, marginBottom: 4 }}>
        Activează verificarea în doi pași
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        Contul tău are acces la date financiare sau administrative, așa că pe lângă parolă ai nevoie de un cod de pe telefon.
      </Typography.Paragraph>

      <Steps
        size="small"
        current={coduri ? 2 : 0}
        style={{ marginBlock: 16 }}
        items={[{ title: 'Scanează' }, { title: 'Confirmă' }, { title: 'Salvează codurile' }]}
      />

      {!configurare ? (
        <Skeleton active />
      ) : coduri ? (
        <>
          <Alert
            type="warning"
            showIcon
            title="Păstrează aceste coduri într-un loc sigur"
            description="Dacă pierzi telefonul, te poți autentifica o singură dată cu fiecare cod. Nu vor mai fi afișate."
            style={{ marginBottom: 16 }}
          />
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: '1fr 1fr',
              gap: 8,
              padding: 16,
              background: '#fff',
              border: '1px solid #e5e7eb',
              borderRadius: 8,
              fontFamily: 'Consolas, monospace',
              fontSize: 15,
            }}
          >
            {coduri.map((c) => (
              <span key={c}>{c}</span>
            ))}
          </div>
          <Flex gap={8} style={{ marginTop: 16 }}>
            <Button
              icon={<CopyOutlined />}
              onClick={() => navigator.clipboard.writeText(coduri.join('\n')).then(() => message.success('Codurile au fost copiate.'))}
            >
              Copiază
            </Button>
            <Button type="primary" block onClick={continua}>
              Am salvat codurile, continuă
            </Button>
          </Flex>
        </>
      ) : (
        <>
          <Flex gap={16} align="center" style={{ marginBottom: 12 }}>
            <QRCode value={configurare.uriOtp} size={152} bordered style={{ background: '#fff', flex: 'none' }} />
            <Typography.Text type="secondary" style={{ fontSize: 13 }}>
              Scanează codul cu Google Authenticator, Microsoft Authenticator sau altă aplicație TOTP. Dacă nu poți scana,
              introdu manual cheia de mai jos.
            </Typography.Text>
          </Flex>
          <Typography.Paragraph
            copyable={{ text: configurare.cheie.replaceAll(' ', '') }}
            style={{ fontFamily: 'Consolas, monospace', fontSize: 14, marginBottom: 16, letterSpacing: 0.5 }}
          >
            {configurare.cheie}
          </Typography.Paragraph>
          {eroare && <Alert type="error" showIcon title={eroare} style={{ marginBottom: 16 }} />}
          <Form layout="vertical" requiredMark={false} size="large" onFinish={activeaza}>
            <Form.Item name="cod" label="Codul afișat în aplicație" rules={[{ required: true, message: 'Introdu codul de 6 cifre.' }]}>
              <Input.OTP length={6} autoFocus />
            </Form.Item>
            <Button type="primary" htmlType="submit" block loading={seIncarca}>
              Activează
            </Button>
          </Form>
        </>
      )}
    </CadruAutentificare>
  )
}
