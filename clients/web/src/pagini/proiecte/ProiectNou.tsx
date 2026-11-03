import { App, Button, Col, Drawer, Form, Input, Row, Space } from 'antd'
import { EroareApi } from '../../api/client'
import { useCreeazaProiect } from '../../api/interogari'
import type { CreeazaProiect } from '../../api/tipuri'

interface Props {
  deschis: boolean
  laInchidere: () => void
  laCreare: (id: string) => void
}

export function ProiectNou({ deschis, laInchidere, laCreare }: Props) {
  const [form] = Form.useForm<CreeazaProiect>()
  const creeaza = useCreeazaProiect()
  const { message } = App.useApp()

  const salveaza = async () => {
    const valori = await form.validateFields()
    try {
      const { id } = await creeaza.mutateAsync({ ...valori, cuiBeneficiar: valori.cuiBeneficiar || null })
      message.success(`Proiectul ${valori.cod.toUpperCase()} a fost creat.`)
      form.resetFields()
      laCreare(id)
    } catch (e) {
      if (e instanceof EroareApi && e.status === 409) {
        form.setFields([{ name: 'cod', errors: [e.message] }])
      } else {
        message.error((e as Error).message)
      }
    }
  }

  return (
    <Drawer
      title="Proiect nou"
      open={deschis}
      onClose={laInchidere}
      size={560}
      destroyOnHidden
      extra={
        <Space>
          <Button onClick={laInchidere}>Renunță</Button>
          <Button type="primary" loading={creeaza.isPending} onClick={salveaza}>
            Creează proiectul
          </Button>
        </Space>
      }
    >
      <Form form={form} layout="vertical" requiredMark="optional">
        <Row gutter={16}>
          <Col span={10}>
            <Form.Item
              name="cod"
              label="Cod proiect"
              rules={[{ required: true, message: 'Codul este obligatoriu.' }, { max: 40 }]}
              tooltip="Unic în cadrul firmei. Se salvează cu majuscule."
            >
              <Input placeholder="P-2026-007" style={{ textTransform: 'uppercase' }} />
            </Form.Item>
          </Col>
          <Col span={14}>
            <Form.Item name="cuiBeneficiar" label="CUI beneficiar" rules={[{ max: 20 }]}>
              <Input placeholder="RO12345678" />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} placeholder="Ex.: Bloc de locuințe P+4, str. Exemplu 10" />
        </Form.Item>
        <Form.Item name="beneficiar" label="Beneficiar" rules={[{ required: true, message: 'Beneficiarul este obligatoriu.' }, { max: 300 }]}>
          <Input />
        </Form.Item>
        <Form.Item name="adresaSantier" label="Adresa șantierului" rules={[{ required: true, message: 'Adresa este obligatorie.' }, { max: 500 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
        </Form.Item>
      </Form>
    </Drawer>
  )
}
