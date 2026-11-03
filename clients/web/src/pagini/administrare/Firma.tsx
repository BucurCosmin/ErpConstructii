import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { App, Button, Col, Form, Input, Row, Skeleton, Switch, Typography } from 'antd'
import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { cheiAdmin, useFirmaActiva, useModificaFirma } from '../../api/administrare'
import type { DateFirma } from '../../api/tipuri'
import { chei, usePermisiuni } from '../../api/interogari'

type FormularFirma = Omit<DateFirma, 'id'>

/** Datele firmei active folosite în antetul comenzilor, facturilor și situațiilor. */
export function Firma() {
  const { data, isLoading } = useFirmaActiva()
  const modifica = useModificaFirma()
  const permisiuni = usePermisiuni()
  const qc = useQueryClient()
  const { message } = App.useApp()
  const [form] = Form.useForm<FormularFirma>()
  const poateEdita = permisiuni.data?.includes('administrare.editare') ?? false

  useEffect(() => {
    if (data) {
      form.setFieldsValue({
        denumire: data.denumire,
        cui: data.cui ?? '',
        nrRegCom: data.nrRegCom ?? '',
        adresa: {
          ...data.adresa,
          judet: data.adresa.judet ?? '',
          codPostal: data.adresa.codPostal ?? '',
          tara: data.adresa.tara || 'RO',
        },
        telefon: data.telefon ?? '',
        fax: data.fax ?? '',
        email: data.email ?? '',
        website: data.website ?? '',
        conturiBancare: data.conturiBancare,
      })
    }
  }, [data, form])

  if (isLoading) return <Skeleton active />

  return (
    <Form
      form={form}
      layout="vertical"
      style={{ maxWidth: 920, marginTop: 8 }}
      disabled={!poateEdita}
      onFinish={async (v) => {
        try {
          await modifica.mutateAsync({
            ...v,
            cui: v.cui?.trim() || null,
            nrRegCom: v.nrRegCom?.trim() || null,
            adresa: {
              strada: v.adresa?.strada?.trim() ?? '',
              localitate: v.adresa?.localitate?.trim() ?? '',
              judet: v.adresa?.judet?.trim() || null,
              codPostal: v.adresa?.codPostal?.trim() || null,
              tara: v.adresa?.tara?.trim() || 'RO',
            },
            telefon: v.telefon?.trim() || null,
            fax: v.fax?.trim() || null,
            email: v.email?.trim() || null,
            website: v.website?.trim() || null,
            conturiBancare: (v.conturiBancare ?? [])
              .filter((c) => c.banca?.trim() && c.iban?.trim())
              .map((c) => ({
                banca: c.banca.trim(),
                iban: c.iban.trim(),
                moneda: c.moneda?.trim() || 'RON',
                principal: c.principal ?? false,
              })),
          })
          await Promise.all([
            qc.invalidateQueries({ queryKey: chei.profil }),
            qc.invalidateQueries({ queryKey: cheiAdmin.firma }),
          ])
          message.success('Datele firmei au fost salvate.')
        } catch (e) {
          message.error((e as Error).message)
        }
      }}
    >
      <Typography.Title level={5}>Identificare fiscală</Typography.Title>
      <Row gutter={16}>
        <Col xs={24} md={14}>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="cui" label="CUI" rules={[{ max: 20 }]}>
            <Input placeholder="RO12345678" />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="nrRegCom" label="Nr. Registrul Comerțului" rules={[{ max: 40 }]}>
            <Input placeholder="J13/123/2020" />
          </Form.Item>
        </Col>
      </Row>

      <Typography.Title level={5}>Adresă și contact</Typography.Title>
      <Row gutter={16}>
        <Col xs={24} md={12}>
          <Form.Item name={['adresa', 'strada']} label="Stradă, număr, bloc">
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={6}>
          <Form.Item name={['adresa', 'localitate']} label="Localitate">
            <Input />
          </Form.Item>
        </Col>
        <Col xs={12} md={3}>
          <Form.Item name={['adresa', 'judet']} label="Județ">
            <Input placeholder="CT" maxLength={3} />
          </Form.Item>
        </Col>
        <Col xs={12} md={3}>
          <Form.Item name={['adresa', 'codPostal']} label="Cod poștal">
            <Input maxLength={10} />
          </Form.Item>
        </Col>
        <Col xs={24} md={4}>
          <Form.Item name={['adresa', 'tara']} label="Țară (cod)">
            <Input maxLength={2} placeholder="RO" />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="telefon" label="Telefon" rules={[{ max: 60 }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="fax" label="Fax" rules={[{ max: 60 }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="email" label="E-mail" rules={[{ type: 'email', message: 'Adresa de e-mail nu este validă.' }, { max: 256 }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={5}>
          <Form.Item name="website" label="Website" rules={[{ max: 256 }]}>
            <Input />
          </Form.Item>
        </Col>
      </Row>

      <Typography.Title level={5}>Conturi bancare</Typography.Title>
      <Form.List name="conturiBancare">
        {(fields, { add, remove }) => (
          <>
            {fields.map((field) => (
              <Row key={field.key} gutter={12} align="middle">
                <Col xs={24} md={7}>
                  <Form.Item {...field} name={[field.name, 'banca']} label="Banca" rules={[{ required: true, message: 'Banca este obligatorie.' }, { max: 200 }]}>
                    <Input />
                  </Form.Item>
                </Col>
                <Col xs={24} md={9}>
                  <Form.Item {...field} name={[field.name, 'iban']} label="IBAN" rules={[{ required: true, message: 'IBAN-ul este obligatoriu.' }, { max: 34 }]}>
                    <Input />
                  </Form.Item>
                </Col>
                <Col xs={12} md={3}>
                  <Form.Item {...field} name={[field.name, 'moneda']} label="Monedă" rules={[{ required: true }]}>
                    <Input maxLength={3} />
                  </Form.Item>
                </Col>
                <Col xs={8} md={3}>
                  <Form.Item {...field} name={[field.name, 'principal']} label="Principal" valuePropName="checked">
                    <Switch />
                  </Form.Item>
                </Col>
                <Col xs={4} md={2}>
                  <Button
                    danger
                    type="text"
                    icon={<DeleteOutlined />}
                    aria-label="Șterge contul bancar"
                    onClick={() => remove(field.name)}
                  />
                </Col>
              </Row>
            ))}
            <Button
              type="dashed"
              icon={<PlusOutlined />}
              disabled={!poateEdita}
              onClick={() => add({ banca: '', iban: '', moneda: 'RON', principal: false })}
            >
              Adaugă cont bancar
            </Button>
          </>
        )}
      </Form.List>

      <Button type="primary" htmlType="submit" loading={modifica.isPending} disabled={!poateEdita}>
        Salvează datele firmei
      </Button>
    </Form>
  )
}
