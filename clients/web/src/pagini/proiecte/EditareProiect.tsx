import { App, Button, Col, DatePicker, Drawer, Form, Input, Row, Select, Space } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect } from 'react'
import { EroareApi } from '../../api/client'
import { useModificaProiect } from '../../api/proiecte'
import type { Proiect, StareProiect } from '../../api/tipuri'
import { stariProiect } from '../../util/format'

interface Valori {
  cod: string
  denumire: string
  beneficiar: string
  cuiBeneficiar?: string
  adresaSantier?: string
  stare: StareProiect
  dataInceput?: Dayjs | null
  termenContractual?: Dayjs | null
}

/** Editarea completă a proiectului (D-38), pentru administratorul firmei: toate datele, starea și datele contractuale. */
export function EditareProiect({ proiect, deschis, laInchidere }: { proiect?: Proiect; deschis: boolean; laInchidere: () => void }) {
  const [form] = Form.useForm<Valori>()
  const modifica = useModificaProiect()
  const { message } = App.useApp()

  useEffect(() => {
    if (deschis && proiect)
      form.setFieldsValue({
        ...proiect,
        cuiBeneficiar: proiect.cuiBeneficiar ?? undefined,
        dataInceput: proiect.dataInceput ? dayjs(proiect.dataInceput) : null,
        termenContractual: proiect.termenContractual ? dayjs(proiect.termenContractual) : null,
      })
  }, [deschis, proiect, form])

  const salveaza = async () => {
    if (!proiect) return
    const v = await form.validateFields()
    try {
      await modifica.mutateAsync({
        id: proiect.id,
        date: {
          cod: v.cod.trim(), denumire: v.denumire.trim(), beneficiar: v.beneficiar.trim(), cuiBeneficiar: v.cuiBeneficiar?.trim() || null,
          adresaSantier: v.adresaSantier?.trim() || null, stare: v.stare,
          dataInceput: v.dataInceput?.format('YYYY-MM-DD') ?? null, termenContractual: v.termenContractual?.format('YYYY-MM-DD') ?? null,
        },
      })
      message.success('Proiectul a fost modificat.')
      laInchidere()
    } catch (e) {
      if (e instanceof EroareApi && e.status === 409) form.setFields([{ name: 'cod', errors: [e.message] }])
      else message.error((e as Error).message)
    }
  }

  return (
    <Drawer title={`Editează proiectul ${proiect?.cod ?? ''}`} open={deschis} onClose={laInchidere} size={600} destroyOnHidden
      extra={<Space><Button onClick={laInchidere}>Renunță</Button><Button type="primary" loading={modifica.isPending} onClick={() => void salveaza()}>Salvează</Button></Space>}>
      <Form form={form} layout="vertical" requiredMark="optional">
        <Row gutter={16}>
          <Col span={10}>
            <Form.Item name="cod" label="Cod proiect" rules={[{ required: true, whitespace: true, message: 'Codul este obligatoriu.' }, { max: 40 }]}
              tooltip="Unic în firmă. Documentele deja emise păstrează codul vechi.">
              <Input style={{ textTransform: 'uppercase' }} />
            </Form.Item>
          </Col>
          <Col span={14}>
            <Form.Item name="stare" label="Stare" rules={[{ required: true }]}>
              <Select options={Object.entries(stariProiect).map(([v, s]) => ({ value: v, label: s.eticheta }))} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
        </Form.Item>
        <Row gutter={16}>
          <Col span={14}>
            <Form.Item name="beneficiar" label="Beneficiar" rules={[{ required: true, whitespace: true, message: 'Beneficiarul este obligatoriu.' }, { max: 300 }]}>
              <Input />
            </Form.Item>
          </Col>
          <Col span={10}>
            <Form.Item name="cuiBeneficiar" label="CUI beneficiar" rules={[{ max: 20 }]}>
              <Input />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="adresaSantier" label="Amplasament (adresa proiectului)" rules={[{ max: 500 }]}>
          <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
        </Form.Item>
        <Row gutter={16}>
          <Col span={12}>
            <Form.Item name="dataInceput" label="Data începerii">
              <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="termenContractual" label="Termen contractual" dependencies={['dataInceput']}
              rules={[({ getFieldValue }) => ({
                validator: async (_, t: Dayjs | null) => {
                  const d = getFieldValue('dataInceput') as Dayjs | null
                  if (t && d && t.isBefore(d, 'day')) throw new Error('Termenul este înaintea datei de început.')
                },
              })]}>
              <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Drawer>
  )
}
