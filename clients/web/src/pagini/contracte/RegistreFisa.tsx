import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons'
import { App, Button, Col, DatePicker, Form, Input, Modal, Popconfirm, Row, Select, Space, Table, Tag, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { prioritati, stariActiune, tipuriInstrument, useRandFisa } from '../../api/contracte'
import type { Contract, Registru } from '../../api/contracte'
import { NumarRo } from '../../componente/NumarRo'
import { formatData, formatLei } from '../../util/format'

/** Un câmp din fereastra unui rând de registru. */
interface Camp {
  nume: string
  eticheta: string
  tip?: 'text' | 'textarea' | 'data' | 'numar' | 'alegere'
  optiuni?: { value: string; label: string }[]
  obligatoriu?: boolean
  latime?: number
}

interface ConfigRegistru {
  titlu: string
  adauga: string
  gol: string
  campuri: Camp[]
  coloane: TableColumnsType<Record<string, unknown>>
}

const text = (k: string, titlu: string, latime?: number) => ({ title: titlu, dataIndex: k, width: latime, ellipsis: { showTitle: true } })
const data = (k: string, titlu: string) => ({ title: titlu, dataIndex: k, width: 105, render: (v: string | null) => formatData(v) })

/** Expirarea / scadența apropiată (sub 30 de zile) sau trecută e marcată. */
function Expirare({ v }: { v: string | null }) {
  if (!v) return <>—</>
  const zile = dayjs(v).diff(dayjs(), 'day')
  return <span>{formatData(v)} {zile < 0 ? <Tag color="red">expirat</Tag> : zile <= 30 ? <Tag color="orange">în {zile} zile</Tag> : null}</span>
}

/** Registrele fișei (D-56), în ordinea foilor din fișa Excel. */
export const configurari: Record<Registru, ConfigRegistru> = {
  obligatii: {
    titlu: 'Obligații și termene',
    adauga: 'Obligație',
    gol: 'Nicio obligație: adaugă reperele contractuale (predare amplasament, situații, plăți, garanții, recepție).',
    campuri: [
      { nume: 'denumire', eticheta: 'Obligație / reper', obligatoriu: true, latime: 24 },
      { nume: 'temei', eticheta: 'Temei contractual (ex. TCS art. 5.11)', latime: 12 },
      { nume: 'dataTermen', eticheta: 'Data termenului (dacă e una anume)', tip: 'data', latime: 12 },
      { nume: 'termen', eticheta: 'Termen / condiție', tip: 'textarea', latime: 24 },
      { nume: 'stare', eticheta: 'Stare (ex. De verificat, În derulare, Închis)', latime: 12 },
      { nume: 'responsabil', eticheta: 'Responsabil', latime: 12 },
      { nume: 'observatii', eticheta: 'Observații / acțiune de verificat', tip: 'textarea', latime: 24 },
    ],
    coloane: [
      text('denumire', 'Obligație / reper', 240), text('temei', 'Temei', 120),
      { title: 'Termen / condiție', render: (_, r) => <>{r.termen as string}{r.dataTermen ? <Typography.Text type="secondary"> ({formatData(r.dataTermen as string)})</Typography.Text> : null}</> },
      text('stare', 'Stare', 120), text('responsabil', 'Responsabil', 130), text('observatii', 'Observații'),
    ],
  },
  actiuni: {
    titlu: 'Acțiuni',
    adauga: 'Acțiune',
    gol: 'Nicio acțiune deschisă.',
    campuri: [
      { nume: 'descriere', eticheta: 'Acțiune', tip: 'textarea', obligatoriu: true, latime: 24 },
      { nume: 'prioritate', eticheta: 'Prioritate', tip: 'alegere', optiuni: Object.entries(prioritati).map(([value, p]) => ({ value, label: p.eticheta })), latime: 8 },
      { nume: 'stare', eticheta: 'Stare', tip: 'alegere', optiuni: Object.entries(stariActiune).map(([value, p]) => ({ value, label: p.eticheta })), latime: 8 },
      { nume: 'dataTermen', eticheta: 'Termen', tip: 'data', latime: 8 },
      { nume: 'responsabil', eticheta: 'Responsabil', latime: 24 },
      { nume: 'observatii', eticheta: 'Observații', tip: 'textarea', latime: 24 },
    ],
    coloane: [
      { title: 'Nr.', dataIndex: 'ordine', width: 50 },
      text('descriere', 'Acțiune'),
      { title: 'Prioritate', dataIndex: 'prioritate', width: 100, render: (v: keyof typeof prioritati) => <Tag color={prioritati[v].culoare}>{prioritati[v].eticheta}</Tag> },
      { title: 'Stare', dataIndex: 'stare', width: 100, render: (v: keyof typeof stariActiune) => <Tag color={stariActiune[v].culoare}>{stariActiune[v].eticheta}</Tag> },
      text('responsabilNume', 'Responsabil', 130),
      { title: 'Termen', dataIndex: 'termen', width: 130, render: (v: string | null, r) => (r.stare === 'Inchisa' ? formatData(v) : <Expirare v={v} />) },
      text('observatii', 'Observații', 220),
    ],
  },
  faze: {
    titlu: 'Termene de execuție',
    adauga: 'Fază',
    gol: 'Nicio fază: adaugă etapele (execuție, fabricație, livrări, montaj, recepție).',
    campuri: [
      { nume: 'denumire', eticheta: 'Fază / reper', obligatoriu: true, latime: 24 },
      { nume: 'start', eticheta: 'Start contractual', tip: 'data', latime: 12 },
      { nume: 'final', eticheta: 'Final contractual', tip: 'data', latime: 12 },
      { nume: 'conditii', eticheta: 'Durată / condiții', latime: 24 },
      { nume: 'stare', eticheta: 'Stare', latime: 12 },
      { nume: 'observatii', eticheta: 'Document / observație', tip: 'textarea', latime: 24 },
    ],
    coloane: [
      text('denumire', 'Fază / reper', 220),
      { title: 'Start', dataIndex: 'start', width: 105, render: (v: string | null) => v ? formatData(v) : <Typography.Text type="secondary">de confirmat</Typography.Text> },
      { title: 'Final', dataIndex: 'final', width: 105, render: (v: string | null) => v ? formatData(v) : <Typography.Text type="secondary">de confirmat</Typography.Text> },
      text('conditii', 'Durată / condiții', 200), text('stare', 'Stare', 160), text('observatii', 'Observații'),
    ],
  },
  riscuri: {
    titlu: 'Aspecte de clarificat',
    adauga: 'Aspect',
    gol: 'Niciun aspect de clarificat.',
    campuri: [
      { nume: 'element', eticheta: 'Element', obligatoriu: true, latime: 24 },
      { nume: 'prevedere', eticheta: 'Prevedere contractuală / verificare', tip: 'textarea', latime: 24 },
      { nume: 'stare', eticheta: 'Status / risc', tip: 'textarea', latime: 24 },
    ],
    coloane: [text('element', 'Element', 220), text('prevedere', 'Prevedere / verificare'), text('stare', 'Status / risc')],
  },
  surse: {
    titlu: 'Baza documentară',
    adauga: 'Document',
    gol: 'Niciun document în baza documentară.',
    campuri: [
      { nume: 'denumire', eticheta: 'Document', obligatoriu: true, latime: 16 },
      { nume: 'dataTermen', eticheta: 'Data', tip: 'data', latime: 8 },
      { nume: 'observatii', eticheta: 'Observații', tip: 'textarea', latime: 24 },
    ],
    coloane: [text('document', 'Document', 320), data('data', 'Data'), text('observatii', 'Observații')],
  },
  instrumente: {
    titlu: 'Instrumente de garanție și plată',
    adauga: 'Instrument',
    gol: 'Niciun instrument: adaugă scrisorile de garanție bancară (avans, bună execuție, tehnică) și biletele la ordin.',
    campuri: [
      { nume: 'tip', eticheta: 'Tip', tip: 'alegere', optiuni: Object.entries(tipuriInstrument).map(([value, label]) => ({ value, label })), latime: 12 },
      { nume: 'numar', eticheta: 'Număr', latime: 12 },
      { nume: 'emitent', eticheta: 'Emitent (banca)', latime: 12 },
      { nume: 'beneficiar', eticheta: 'Membrul asocierii beneficiar', latime: 12 },
      { nume: 'valoare', eticheta: 'Valoare', tip: 'numar', latime: 8 },
      { nume: 'moneda', eticheta: 'Moneda', tip: 'alegere', optiuni: ['EUR', 'RON'].map((m) => ({ value: m, label: m })), latime: 4 },
      { nume: 'dataEmitere', eticheta: 'Emisă la', tip: 'data', latime: 6 },
      { nume: 'dataExpirare', eticheta: 'Expiră / scadentă la', tip: 'data', latime: 6 },
      { nume: 'descriere', eticheta: 'Descriere', latime: 24 },
      { nume: 'stare', eticheta: 'Stare (ex. Primită în original, De verificat)', latime: 24 },
      { nume: 'observatii', eticheta: 'Observații', tip: 'textarea', latime: 24 },
    ],
    coloane: [
      { title: 'Tip', dataIndex: 'tip', width: 160, render: (v: keyof typeof tipuriInstrument) => tipuriInstrument[v] },
      text('numar', 'Nr.', 120), text('emitent', 'Emitent', 170), text('beneficiar', 'Beneficiar', 150),
      { title: 'Valoare', width: 150, align: 'right', render: (_, r) => (r.valoare === null ? '—' : `${formatLei(r.valoare as number)} ${(r.moneda as string) ?? ''}`) },
      data('dataEmitere', 'Emisă'),
      { title: 'Expiră / scadență', dataIndex: 'dataExpirare', width: 160, render: (v: string | null) => <Expirare v={v} /> },
      text('stare', 'Stare'),
    ],
  },
}

/** Valorile rândului în formular (datele ca dayjs) și înapoi în cerere (datele ca AAAA-LL-ZZ). */
const spreFormular = (r: Record<string, unknown>, campuri: Camp[]) =>
  Object.fromEntries(campuri.map((c) => {
    const v = c.nume === 'responsabil' ? (r.responsabilNume ?? r.responsabil)
      : c.nume === 'denumire' && r.document !== undefined ? r.document
        : c.nume === 'dataTermen' && r.data !== undefined && r.document !== undefined ? r.data
          : c.nume === 'dataTermen' && r.termen !== undefined && !('dataTermen' in r) ? r.termen : r[c.nume]
    return [c.nume, c.tip === 'data' && v ? dayjs(v as string) : v ?? undefined]
  }))

const spreCerere = (v: Record<string, unknown>, campuri: Camp[]) =>
  Object.fromEntries(campuri.map((c) => {
    const x = v[c.nume]
    if (c.tip === 'data') return [c.nume, x ? (x as Dayjs).format('YYYY-MM-DD') : null]
    if (typeof x === 'string') return [c.nume, x.trim() || null]
    return [c.nume, x ?? null]
  }))

/** Un registru al fișei: tabelul și fereastra de adăugare / modificare. Se editează oricând (și pe contractele semnate). */
export function RegistruFisa({ contract, registru, poateEdita, randuri }: {
  contract: Contract; registru: Registru; poateEdita: boolean; randuri: Record<string, unknown>[]
}) {
  const { message } = App.useApp()
  const cfg = configurari[registru]
  const salveaza = useRandFisa()
  const [form] = Form.useForm()
  const [deschis, setDeschis] = useState<{ id?: string } | null>(null)

  const deschide = (r?: Record<string, unknown>) => {
    form.resetFields()
    form.setFieldsValue(r ? spreFormular(r, cfg.campuri) : registru === 'actiuni' ? { prioritate: 'Normala', stare: 'Deschisa' } : registru === 'instrumente' ? { tip: 'GarantieAvans', moneda: contract.moneda } : {})
    setDeschis({ id: r?.id as string | undefined })
  }

  const trimite = async () => {
    const v = await form.validateFields()
    try {
      await salveaza.mutateAsync({ proiectId: contract.proiectId, id: contract.id, registru, randId: deschis?.id, date: spreCerere(v, cfg.campuri) })
      setDeschis(null)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const coloane: TableColumnsType<Record<string, unknown>> = [
    ...cfg.coloane,
    ...(poateEdita ? [{
      title: '', key: 'a', width: 70, fixed: 'right' as const,
      render: (_: unknown, r: Record<string, unknown>) => (
        <Space size={0}>
          <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschide(r)} />
          <Popconfirm title="Ștergi rândul?" okText="Șterge" okButtonProps={{ danger: true }} cancelText="Nu"
            onConfirm={() => salveaza.mutateAsync({ proiectId: contract.proiectId, id: contract.id, registru, randId: r.id as string, sterge: true }).catch((e: Error) => message.error(e.message))}>
            <Button size="small" type="text" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    }] : []),
  ]

  return (
    <>
      <Table<Record<string, unknown>> className="tabel-compact" size="small" rowKey="id" pagination={false} dataSource={randuri} columns={coloane}
        scroll={{ x: 900 }} locale={{ emptyText: cfg.gol }}
        title={() => (
          <Space>
            <Typography.Text strong>{cfg.titlu}</Typography.Text>
            {poateEdita && <Button size="small" icon={<PlusOutlined />} onClick={() => deschide()}>{cfg.adauga}</Button>}
          </Space>
        )} />
      <Modal open={!!deschis} title={`${deschis?.id ? 'Modifică' : 'Adaugă'}: ${cfg.adauga.toLowerCase()}`} okText="Salvează" cancelText="Renunță" width={720}
        confirmLoading={salveaza.isPending} onOk={() => void trimite()} onCancel={() => setDeschis(null)} destroyOnHidden forceRender>
        <Form form={form} layout="vertical">
          <Row gutter={12}>
            {cfg.campuri.map((c) => (
              <Col key={c.nume} xs={24} md={c.latime ?? 24}>
                <Form.Item name={c.nume} label={c.eticheta} rules={c.obligatoriu ? [{ required: true, whitespace: true, message: 'Câmpul e obligatoriu.' }] : undefined}>
                  {c.tip === 'textarea' ? <Input.TextArea autoSize={{ minRows: 1, maxRows: 4 }} maxLength={2000} />
                    : c.tip === 'data' ? <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
                      : c.tip === 'numar' ? <NumarRo min={0} style={{ width: '100%' }} />
                        : c.tip === 'alegere' ? <Select options={c.optiuni} />
                          : <Input maxLength={500} />}
                </Form.Item>
              </Col>
            ))}
          </Row>
        </Form>
      </Modal>
    </>
  )
}
