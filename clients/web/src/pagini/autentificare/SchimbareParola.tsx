import { KeyOutlined, LockOutlined } from '@ant-design/icons'
import { Alert, Button, Form, Input, Typography } from 'antd'
import { useState } from 'react'
import { managerAutentificare, urlRevenireValid } from '../../api/autentificare'
import { apiCont } from '../../api/client'
import { CadruAutentificare } from './CadruAutentificare'

/** Schimbarea obligatorie a parolei temporare primite de la administrator. */
export function SchimbareParola() {
  const [eroare, setEroare] = useState<string | null>(null)
  const [seIncarca, setSeIncarca] = useState(false)
  const revenire = new URLSearchParams(window.location.search).get('ReturnUrl')

  const salveaza = async ({ parolaNoua }: { parolaNoua: string }) => {
    setEroare(null)
    setSeIncarca(true)
    try {
      await apiCont.post('/api/cont/parola-noua', { parolaNoua })
      if (urlRevenireValid(revenire)) window.location.assign(revenire)
      else await managerAutentificare.signinRedirect()
    } catch (e) {
      setEroare((e as Error).message)
      setSeIncarca(false)
    }
  }

  return (
    <CadruAutentificare>
      <KeyOutlined style={{ fontSize: 36, color: '#1f5fbf' }} />
      <Typography.Title level={3} style={{ marginTop: 12, marginBottom: 4 }}>
        Alege o parolă nouă
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        Te-ai autentificat cu o parolă temporară. Alege una personală: cel puțin 12 caractere, cu litere mari, mici și cifre.
      </Typography.Paragraph>
      {eroare && <Alert type="error" showIcon title={eroare} style={{ marginBottom: 16 }} />}
      <Form layout="vertical" requiredMark={false} size="large" onFinish={salveaza}>
        <Form.Item
          name="parolaNoua"
          label="Parola nouă"
          rules={[
            { required: true, message: 'Introdu parola nouă.' },
            { min: 12, message: 'Cel puțin 12 caractere.' },
          ]}
          hasFeedback
        >
          <Input.Password prefix={<LockOutlined />} autoComplete="new-password" autoFocus />
        </Form.Item>
        <Form.Item
          name="confirmare"
          label="Confirmă parola"
          dependencies={['parolaNoua']}
          hasFeedback
          rules={[
            { required: true, message: 'Confirmă parola.' },
            ({ getFieldValue }) => ({
              validator: (_, v) => (!v || v === getFieldValue('parolaNoua') ? Promise.resolve() : Promise.reject(new Error('Parolele nu coincid.'))),
            }),
          ]}
        >
          <Input.Password prefix={<LockOutlined />} autoComplete="new-password" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block loading={seIncarca}>
          Salvează și continuă
        </Button>
      </Form>
    </CadruAutentificare>
  )
}
