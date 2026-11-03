import { DeleteOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { App, Button, Checkbox, Col, Drawer, Form, Input, Row, Segmented, Space, Switch, Table, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useCreeazaFurnizor, useFurnizori, useModificaFurnizor, useSchimbaActivareaFurnizorului } from '../../api/parteneri'
import type { DatePartener, Partener } from '../../api/tipuri'
import { usePermisiuni } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'

const dateGoale: DatePartener = {
  denumire: '',
  cui: '',
  nrRegCom: '',
  platitorTva: false,
  adresa: { strada: '', localitate: '', judet: '', codPostal: '', tara: 'RO' },
  telefon: '',
  email: '',
  persoanaContact: '',
  telefonContact: '',
  observatii: '',
  conturiBancare: [],
  esteClient: false,
  esteSubantreprenor: false,
}

type Filtru = 'active' | 'toate'

export function Furnizori() {
  const [filtru, setFiltru] = useState<Filtru>('active')
  const [cauta, setCauta] = useState('')
  const [deschis, setDeschis] = useState(false)
  const [furnizorEditat, setFurnizorEditat] = useState<Partener | null>(null)
  const [form] = Form.useForm<DatePartener>()
  const { data = [], isLoading, error } = useFurnizori(true)
  const creare = useCreeazaFurnizor()
  const modificare = useModificaFurnizor()
  const schimbaActivarea = useSchimbaActivareaFurnizorului()
  const permisiuni = usePermisiuni()
  const { message } = App.useApp()
  const poateCrea = permisiuni.data?.includes('aprovizionare.creare') ?? false
  const poateEdita = permisiuni.data?.includes('aprovizionare.editare') ?? false
  const poateDezactiva = permisiuni.data?.includes('aprovizionare.anulare') ?? false

  useEffect(() => {
    if (!deschis) return
    if (furnizorEditat) {
      form.setFieldsValue({
        ...furnizorEditat,
        cui: furnizorEditat.cui ?? '',
        nrRegCom: furnizorEditat.nrRegCom ?? '',
        adresa: {
          ...furnizorEditat.adresa,
          judet: furnizorEditat.adresa.judet ?? '',
          codPostal: furnizorEditat.adresa.codPostal ?? '',
          tara: furnizorEditat.adresa.tara || 'RO',
        },
        telefon: furnizorEditat.telefon ?? '',
        email: furnizorEditat.email ?? '',
        persoanaContact: furnizorEditat.persoanaContact ?? '',
        telefonContact: furnizorEditat.telefonContact ?? '',
        observatii: furnizorEditat.observatii ?? '',
      })
    } else {
      form.setFieldsValue(dateGoale)
    }
  }, [deschis, form, furnizorEditat])

  const filtrati = useMemo(() => {
    const termen = cauta.trim().toLocaleLowerCase('ro')
    return data.filter((p) => {
      if (filtru === 'active' && !p.activ) return false
      if (!termen) return true
      return [
        p.denumire,
        p.cui ?? '',
        p.nrRegCom ?? '',
        p.adresa.localitate,
        p.telefon ?? '',
        p.email ?? '',
        p.persoanaContact ?? '',
      ].some((v) => v.toLocaleLowerCase('ro').includes(termen))
    })
  }, [data, filtru, cauta])

  const deschideFurnizor = (furnizor: Partener | null) => {
    setFurnizorEditat(furnizor)
    setDeschis(true)
  }

  const salveaza = async (v: DatePartener) => {
    const date: DatePartener = {
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
      email: v.email?.trim() || null,
      persoanaContact: v.persoanaContact?.trim() || null,
      telefonContact: v.telefonContact?.trim() || null,
      observatii: v.observatii?.trim() || null,
      conturiBancare: (v.conturiBancare ?? [])
        .filter((c) => c.banca?.trim() && c.iban?.trim())
        .map((c) => ({
          banca: c.banca.trim(),
          iban: c.iban.trim(),
          moneda: c.moneda?.trim() || 'RON',
          principal: c.principal ?? false,
        })),
    }

    try {
      if (furnizorEditat) {
        await modificare.mutateAsync({ id: furnizorEditat.id, ...date })
        message.success('Fișa furnizorului a fost actualizată.')
      } else {
        await creare.mutateAsync(date)
        message.success('Furnizorul a fost adăugat.')
      }
      setDeschis(false)
      setFurnizorEditat(null)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const schimbaStarea = async (furnizor: Partener) => {
    try {
      await schimbaActivarea.mutateAsync({ id: furnizor.id, activ: !furnizor.activ })
      message.success(furnizor.activ ? 'Furnizorul a fost dezactivat.' : 'Furnizorul a fost reactivat.')
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <>
      <AntetPagina
        titlu="Furnizori"
        subtitlu="Registrul furnizorilor, cu datele fiscale, de contact și conturile bancare folosite pe comenzi și facturi."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Furnizori' }]}
        actiuni={
          <Button type="primary" icon={<PlusOutlined />} disabled={!poateCrea} onClick={() => deschideFurnizor(null)}>
            Furnizor nou
          </Button>
        }
      />

      {error && <Typography.Text type="danger">{(error as Error).message}</Typography.Text>}

      <Space wrap style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }}>
        <Segmented<Filtru>
          value={filtru}
          onChange={setFiltru}
          options={[
            { value: 'active', label: 'Active' },
            { value: 'toate', label: 'Toți, inclusiv inactivi' },
          ]}
        />
        <Input
          prefix={<SearchOutlined />}
          placeholder="Denumire, CUI, localitate, contact…"
          allowClear
          value={cauta}
          onChange={(e) => setCauta(e.target.value)}
          style={{ width: 320 }}
        />
      </Space>

      <Table<Partener>
        rowKey="id"
        loading={isLoading}
        dataSource={filtrati}
        pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => `${n} furnizori` }}
        scroll={{ x: 900 }}
        columns={[
          {
            title: 'Furnizor',
            dataIndex: 'denumire',
            sorter: (a, b) => a.denumire.localeCompare(b.denumire),
            render: (denumire: string, p) => (
              <>
                <Typography.Text strong>{denumire}</Typography.Text>
                <br />
                <Typography.Text type="secondary">{p.codFiscal ?? 'Fără CUI'}</Typography.Text>
              </>
            ),
          },
          {
            title: 'Localitate',
            render: (_, p) => [p.adresa.codPostal, p.adresa.localitate].filter(Boolean).join(' '),
          },
          {
            title: 'Contact',
            render: (_, p) => (
              <>
                <div>{p.telefon ?? p.telefonContact ?? '—'}</div>
                <Typography.Text type="secondary">{p.email ?? '—'}</Typography.Text>
              </>
            ),
          },
          {
            title: 'Roluri',
            render: (_, p) => (
              <Space size={4}>
                {p.esteClient && <Tag>Client</Tag>}
                {p.esteSubantreprenor && <Tag>Subantreprenor</Tag>}
              </Space>
            ),
          },
          {
            title: 'Stare',
            dataIndex: 'activ',
            render: (activ: boolean) => <Tag color={activ ? 'green' : 'default'}>{activ ? 'Activ' : 'Inactiv'}</Tag>,
          },
          {
            title: '',
            key: 'actiuni',
            width: 210,
            render: (_, p) => (
              <Space>
                <Button type="link" disabled={!poateEdita} onClick={() => deschideFurnizor(p)}>
                  Editează
                </Button>
                <Button
                  type="link"
                  danger={p.activ}
                  disabled={p.activ ? !poateDezactiva : !poateEdita}
                  loading={schimbaActivarea.isPending}
                  onClick={() => void schimbaStarea(p)}
                >
                  {p.activ ? 'Dezactivează' : 'Reactivează'}
                </Button>
              </Space>
            ),
          },
        ]}
      />

      <Drawer
        title={furnizorEditat ? 'Modifică furnizorul' : 'Furnizor nou'}
        open={deschis}
        onClose={() => {
          setDeschis(false)
          setFurnizorEditat(null)
        }}
        width={680}
        destroyOnHidden
        extra={
          <Button
            type="primary"
            onClick={() => form.submit()}
            loading={creare.isPending || modificare.isPending}
            disabled={furnizorEditat ? !poateEdita : !poateCrea}
          >
            Salvează
          </Button>
        }
      >
        <Form form={form} layout="vertical" disabled={furnizorEditat ? !poateEdita : !poateCrea} onFinish={salveaza}>
          <Row gutter={16}>
            <Col span={16}>
              <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="platitorTva" label="Plătitor de TVA" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="cui" label="CUI" rules={[{ max: 20 }]}>
                <Input placeholder="RO12345678" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="nrRegCom" label="Nr. Registrul Comerțului" rules={[{ max: 40 }]}>
                <Input placeholder="J13/123/2020" />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name={['adresa', 'strada']} label="Stradă, număr, bloc">
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name={['adresa', 'localitate']} label="Localitate">
                <Input />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name={['adresa', 'judet']} label="Județ">
                <Input maxLength={3} placeholder="CT" />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name={['adresa', 'codPostal']} label="Cod poștal">
                <Input maxLength={10} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="telefon" label="Telefon" rules={[{ max: 60 }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="email" label="E-mail" rules={[{ type: 'email', message: 'Adresa de e-mail nu este validă.' }, { max: 256 }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="persoanaContact" label="Persoană de contact" rules={[{ max: 200 }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="telefonContact" label="Telefon contact" rules={[{ max: 60 }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item label="Alte roluri">
                <Space>
                  <Form.Item name="esteClient" valuePropName="checked" noStyle>
                    <Checkbox>Client</Checkbox>
                  </Form.Item>
                  <Form.Item name="esteSubantreprenor" valuePropName="checked" noStyle>
                    <Checkbox>Subantreprenor</Checkbox>
                  </Form.Item>
                </Space>
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="observatii" label="Observații">
                <Input.TextArea rows={3} maxLength={2000} />
              </Form.Item>
            </Col>
          </Row>

          <Typography.Title level={5}>Conturi bancare</Typography.Title>
          <Form.List name="conturiBancare">
            {(fields, { add, remove }) => (
              <>
                {fields.map((field) => (
                  <Row key={field.key} gutter={8} align="middle">
                    <Col span={8}>
                      <Form.Item {...field} name={[field.name, 'banca']} label="Banca" rules={[{ required: true, message: 'Banca este obligatorie.' }, { max: 200 }]}>
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col span={9}>
                      <Form.Item {...field} name={[field.name, 'iban']} label="IBAN" rules={[{ required: true, message: 'IBAN-ul este obligatoriu.' }, { max: 34 }]}>
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col span={3}>
                      <Form.Item {...field} name={[field.name, 'moneda']} label="Monedă" rules={[{ required: true }]}>
                        <Input maxLength={3} />
                      </Form.Item>
                    </Col>
                    <Col span={2}>
                      <Form.Item {...field} name={[field.name, 'principal']} label="Principal" valuePropName="checked">
                        <Switch />
                      </Form.Item>
                    </Col>
                    <Col span={2}>
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
                <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ banca: '', iban: '', moneda: 'RON', principal: false })}>
                  Adaugă cont bancar
                </Button>
              </>
            )}
          </Form.List>
        </Form>
      </Drawer>
    </>
  )
}
