import { App, Button, Col, Drawer, Form, Input, Radio, Row, Space, Typography } from 'antd'
import { EroareApi } from '../../api/client'
import { useCreeazaProiect } from '../../api/interogari'
import type { CreeazaProiect } from '../../api/tipuri'

interface Props {
  deschis: boolean
  laInchidere: () => void
  laCreare: (id: string) => void
}

/** Proiect nou: o lucrare pentru un beneficiar sau un sediu al firmei (D-47: centru de cost pentru cheltuielile de sediu). */
export function ProiectNou({ deschis, laInchidere, laCreare }: Props) {
  const [form] = Form.useForm<CreeazaProiect>()
  const creeaza = useCreeazaProiect()
  const { message } = App.useApp()
  const tip = Form.useWatch('tip', form) ?? 'Lucrare'
  const sediu = tip === 'Sediu'

  const salveaza = async () => {
    const valori = await form.validateFields()
    try {
      const { id } = await creeaza.mutateAsync({
        ...valori,
        beneficiar: sediu ? null : valori.beneficiar,
        cuiBeneficiar: sediu ? null : valori.cuiBeneficiar || null,
      })
      message.success(`${sediu ? 'Sediul' : 'Proiectul'} ${valori.cod.toUpperCase()} a fost creat.`)
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
      title={sediu ? 'Sediu nou al firmei' : 'Proiect nou'}
      open={deschis}
      onClose={laInchidere}
      size={560}
      destroyOnHidden
      extra={
        <Space>
          <Button onClick={laInchidere}>Renunță</Button>
          <Button type="primary" loading={creeaza.isPending} onClick={salveaza}>
            {sediu ? 'Creează sediul' : 'Creează proiectul'}
          </Button>
        </Space>
      }
    >
      <Form form={form} layout="vertical" requiredMark="optional" initialValues={{ tip: 'Lucrare' }}>
        <Form.Item name="tip" label="Tip">
          <Radio.Group optionType="button" buttonStyle="solid"
            options={[{ value: 'Lucrare', label: 'Lucrare (beneficiar)' }, { value: 'Sediu', label: 'Sediu al firmei' }]} />
        </Form.Item>
        {sediu && (
          <Typography.Paragraph type="secondary">
            Sediul (birouri, bază, depozit) e un centru de cost permanent: pe el se înregistrează necesarele, notele de comandă, facturile și
            bonurile de consum pentru cheltuielile de sediu, cu aceleași rapoarte ca la o lucrare. Locurile din sediu se adaugă în fișa lui.
          </Typography.Paragraph>
        )}
        <Row gutter={16}>
          <Col span={10}>
            <Form.Item
              name="cod"
              label={sediu ? 'Cod sediu' : 'Cod proiect'}
              rules={[{ required: true, message: 'Codul este obligatoriu.' }, { max: 40 }]}
              tooltip="Unic în cadrul firmei. Se salvează cu majuscule."
            >
              <Input placeholder={sediu ? 'SEDIU-CV' : 'P-2026-007'} style={{ textTransform: 'uppercase' }} />
            </Form.Item>
          </Col>
          {!sediu && (
            <Col span={14}>
              <Form.Item name="cuiBeneficiar" label="CUI beneficiar" rules={[{ max: 20 }]}>
                <Input placeholder="RO12345678" />
              </Form.Item>
            </Col>
          )}
        </Row>
        <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} placeholder={sediu ? 'Ex.: Sediul central Cernavodă' : 'Ex.: Bloc de locuințe P+4, str. Exemplu 10'} />
        </Form.Item>
        {!sediu && (
          <Form.Item name="beneficiar" label="Beneficiar" rules={[{ required: true, message: 'Beneficiarul este obligatoriu.' }, { max: 300 }]}>
            <Input />
          </Form.Item>
        )}
        <Form.Item name="adresaSantier" label={sediu ? 'Adresa sediului' : 'Adresa șantierului'} rules={[{ required: true, message: 'Adresa este obligatorie.' }, { max: 500 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
        </Form.Item>
      </Form>
    </Drawer>
  )
}
