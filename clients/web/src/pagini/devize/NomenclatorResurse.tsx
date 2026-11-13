import { LinkOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Checkbox, Col, Descriptions, Form, Input, Modal, Row, Segmented, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useEffect, useState } from 'react'
import { usePermisiuni } from '../../api/interogari'
import { useMapeazaResursa, usePropuneriStoc, useResurseCatalog } from '../../api/resurse'
import type { ResursaCatalog, TipResursa } from '../../api/resurse'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate } from '../../util/format'

const tipuri: Record<TipResursa, string> = {
  Material: 'Material',
  Manopera: 'Manoperă',
  Utilaj: 'Utilaj',
  Transport: 'Transport',
  Echipament: 'Echipament',
}
const culoriTip: Record<TipResursa, string> = { Material: 'blue', Manopera: 'green', Utilaj: 'orange', Transport: 'cyan', Echipament: 'purple' }

/** Legătura resursei în ERP, pe scurt. */
function Legatura({ r }: { r: ResursaCatalog }) {
  if (r.faraMapare) return <Typography.Text type="secondary">fără corespondent</Typography.Text>
  if (r.articolStocDenumire)
    return (
      <span>
        {r.articolStocDenumire}
        {r.factorConversie !== null && r.factorConversie !== 1 && (
          <Typography.Text type="secondary" style={{ fontSize: 11, marginInlineStart: 6 }}>
            1 {r.unitateMasura} = {formatCantitate(r.factorConversie)} {r.articolStocUnitateMasura}
          </Typography.Text>
        )}
      </span>
    )
  if (r.meserie) return <span>{r.meserie}{r.categorie ? `, cat. ${r.categorie}` : ''}</span>
  if (r.utilaj) return <span>{r.utilaj}</span>
  return <Tag color="red">nelegată</Tag>
}

interface ValoriMapare {
  tip: TipResursa
  articolStocId?: string
  factorConversie?: number
  meserie?: string
  categorie?: string
  utilaj?: string
  faraMapare: boolean
}

/** Fereastra de legare a unei resurse: materialul din stoc (cu factorul de conversie), meseria, utilajul sau „fără corespondent”. */
function LegareResursa({ resursa, laInchidere }: { resursa?: ResursaCatalog; laInchidere: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<ValoriMapare>()
  const [cauta, setCauta] = useState('')
  const propuneri = usePropuneriStoc(resursa?.id, cauta)
  const mapeaza = useMapeazaResursa()
  const tip = Form.useWatch('tip', form)
  const faraMapare = Form.useWatch('faraMapare', form)
  const articolId = Form.useWatch('articolStocId', form)
  const articol = propuneri.data?.find((a) => a.id === articolId)
  const umStoc = articol?.unitateMasura ?? (articolId === resursa?.articolStocId ? resursa?.articolStocUnitateMasura : undefined)

  useEffect(() => {
    if (!resursa) return
    setCauta('')
    form.setFieldsValue({
      tip: resursa.tip,
      articolStocId: resursa.articolStocId ?? undefined,
      factorConversie: resursa.factorConversie ?? undefined,
      meserie: resursa.meserie ?? (resursa.tip === 'Manopera' ? resursa.denumire : undefined),
      categorie: resursa.categorie ?? undefined,
      utilaj: resursa.utilaj ?? (resursa.tip === 'Utilaj' || resursa.tip === 'Transport' ? resursa.denumire : undefined),
      faraMapare: resursa.faraMapare,
    })
  }, [resursa, form])

  const salveaza = async () => {
    if (!resursa) return
    const v = await form.validateFields()
    const stoc = v.tip === 'Material' || v.tip === 'Echipament'
    try {
      await mapeaza.mutateAsync({
        id: resursa.id,
        cerere: {
          tip: v.tip,
          faraMapare: v.faraMapare,
          articolStocId: stoc ? v.articolStocId : null,
          factorConversie: stoc ? (v.factorConversie ?? null) : null,
          meserie: v.tip === 'Manopera' ? v.meserie : null,
          categorie: v.tip === 'Manopera' ? v.categorie : null,
          utilaj: v.tip === 'Utilaj' || v.tip === 'Transport' ? v.utilaj : null,
        },
      })
      message.success(`Resursa ${resursa.cod} a fost legată.`)
      laInchidere()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  // Opțiunile: propunerile / rezultatele căutării, plus articolul deja ales (ca să-și păstreze eticheta).
  const optiuni = [
    ...(resursa?.articolStocId && !propuneri.data?.some((a) => a.id === resursa.articolStocId)
      ? [{ value: resursa.articolStocId, label: `${resursa.articolStocDenumire} (${resursa.articolStocUnitateMasura ?? ''})` }]
      : []),
    ...(propuneri.data ?? []).map((a) => ({ value: a.id, label: `${a.cod} — ${a.denumire} (${a.unitateMasura})` })),
  ]

  return (
    <Modal open={!!resursa} title={resursa ? `Leagă resursa ${resursa.cod}` : ''} okText="Salvează" cancelText="Renunță" width={720}
      confirmLoading={mapeaza.isPending} onOk={() => void salveaza()} onCancel={laInchidere} destroyOnHidden>
      {resursa && (
        <>
          <Descriptions size="small" column={2} bordered style={{ marginBottom: 16 }}
            items={[
              { key: 'd', label: 'Denumire', children: resursa.denumire, span: 2 },
              { key: 'u', label: 'UM', children: resursa.unitateMasura },
              { key: 'v', label: 'Folosită', children: `${resursa.utilizari} articole, ${resursa.versiuni} versiuni de deviz` },
            ]} />
          <Form form={form} layout="vertical">
            <Form.Item name="tip" label="Tipul resursei" extra={resursa.tipDedus ? 'Tipul a fost dedus din UM (resursă fără preț în deviz): confirmă-l.' : undefined}>
              <Segmented options={Object.entries(tipuri).map(([value, label]) => ({ value, label }))} />
            </Form.Item>
            <Form.Item name="faraMapare" valuePropName="checked">
              <Checkbox>Fără corespondent în ERP (resursa nu se urmărește, ex. apa industrială)</Checkbox>
            </Form.Item>
            {!faraMapare && (tip === 'Material' || tip === 'Echipament') && (
              <Row gutter={12}>
                <Col span={17}>
                  <Form.Item name="articolStocId" label="Materialul din stoc" rules={[{ required: true, message: 'Alege materialul din stoc.' }]}
                    extra="Propunerile vin după denumirea resursei; scrie ca să cauți alt material.">
                    <Select showSearch filterOption={false} onSearch={setCauta} loading={propuneri.isFetching} options={optiuni}
                      placeholder="Caută materialul" notFoundContent={propuneri.isFetching ? 'Se caută…' : 'Niciun material găsit'} />
                  </Form.Item>
                </Col>
                <Col span={7}>
                  <Form.Item name="factorConversie"
                    label={<Tooltip title="Când materialul din stoc are altă unitate de măsură decât resursa. Gol = automat pentru aceeași unitate sau perechile fixe (to → kg = 1000).">
                      1 {resursa.unitateMasura} = ? {umStoc ?? 'UM stoc'}
                    </Tooltip>}>
                    <NumarRo style={{ width: '100%' }} min={0} placeholder="automat" />
                  </Form.Item>
                </Col>
              </Row>
            )}
            {!faraMapare && tip === 'Manopera' && (
              <Row gutter={12}>
                <Col span={16}><Form.Item name="meserie" label="Meseria" rules={[{ required: true, whitespace: true }]}><Input /></Form.Item></Col>
                <Col span={8}><Form.Item name="categorie" label="Categoria"><Input placeholder="ex. II" /></Form.Item></Col>
              </Row>
            )}
            {!faraMapare && (tip === 'Utilaj' || tip === 'Transport') && (
              <Form.Item name="utilaj" label={tip === 'Utilaj' ? 'Utilajul din ERP' : 'Serviciul de transport'} rules={[{ required: true, whitespace: true }]}>
                <Input />
              </Form.Item>
            )}
          </Form>
        </>
      )}
    </Modal>
  )
}

/**
 * Nomenclatorul de resurse al firmei (REQ-IMP-04, D-55): codurile din devizele importate legate o singură dată de ERP —
 * materialele de articolele din stoc (cu factorul de conversie a UM), manopera de meserii, utilajele și transportul.
 * Legătura cu stocul aduce în catalog prețurile reale de achiziție.
 */
export function NomenclatorResurse() {
  const permisiuni = usePermisiuni()
  const poateLega = permisiuni.data?.some((p) => p === 'ofertare.editare' || p === 'aprovizionare.editare') ?? false
  const [stare, setStare] = useState('nemapate')
  const [tip, setTip] = useState<string>()
  const [cauta, setCauta] = useState('')
  const [deLegat, setDeLegat] = useState<ResursaCatalog>()
  const { data, isFetching, error } = useResurseCatalog({ stare, tip, cauta: cauta.trim() })

  const coloane: TableColumnsType<ResursaCatalog> = [
    { title: 'Cod', dataIndex: 'cod', width: 110, render: (v) => <Typography.Text code>{v}</Typography.Text> },
    { title: 'Denumire', dataIndex: 'denumire', ellipsis: { showTitle: true } },
    { title: 'UM', dataIndex: 'unitateMasura', width: 60 },
    {
      title: 'Tip', dataIndex: 'tip', width: 120,
      render: (t: TipResursa, r) => (
        <Space size={2}>
          <Tag color={culoriTip[t]}>{tipuri[t]}</Tag>
          {r.tipDedus && <Tooltip title="Tip dedus din UM: de confirmat la legare"><Tag>?</Tag></Tooltip>}
        </Space>
      ),
    },
    { title: 'Devize', dataIndex: 'versiuni', width: 70, className: 'numeric' },
    { title: 'Legătura în ERP', ellipsis: true, render: (_, r) => <Legatura r={r} /> },
    ...(poateLega ? [{
      title: '', key: 'a', width: 90,
      render: (_: unknown, r: ResursaCatalog) => (
        <Button size="small" type={r.mapata ? 'text' : 'primary'} icon={<LinkOutlined />} onClick={() => setDeLegat(r)}>
          {r.mapata ? 'Schimbă' : 'Leagă'}
        </Button>
      ),
    }] : []),
  ]

  return (
    <>
      <Card
        styles={{ body: { padding: 0 } }}
        title={
          <Space wrap>
            <Segmented value={stare} onChange={setStare}
              options={[{ value: 'nemapate', label: 'Nelegate' }, { value: 'mapate', label: 'Legate' }, { value: '', label: 'Toate' }]} />
            <Select allowClear placeholder="Tip" style={{ width: 140 }} value={tip} onChange={setTip}
              options={Object.entries(tipuri).map(([value, label]) => ({ value, label }))} />
            <Input.Search allowClear placeholder="Caută cod sau denumire" style={{ width: 260 }} onSearch={setCauta}
              onChange={(e) => !e.target.value && setCauta('')} />
          </Space>
        }
        extra={<Typography.Text type="secondary">{data?.length ?? 0} resurse</Typography.Text>}
      >
        <Alert type="info" showIcon style={{ margin: 12 }}
          title="Fiecare resursă din devize (cod ISDP) se leagă o singură dată: materialele de articolele din stoc, manopera de meserii, utilajele și transportul de denumirea lor din ERP. Devizele importate ulterior găsesc legătura gata, iar catalogul de prețuri arată pentru materialele legate și prețurile reale de achiziție." />
        {error && <Alert type="error" showIcon title={(error as Error).message} style={{ margin: 12 }} />}
        <Table<ResursaCatalog> className="tabel-compact" rowKey="id" size="small" loading={isFetching} dataSource={data} columns={coloane}
          sticky pagination={{ pageSize: 50, showSizeChanger: true, size: 'small', hideOnSinglePage: true }} />
      </Card>
      <LegareResursa resursa={deLegat} laInchidere={() => setDeLegat(undefined)} />
    </>
  )
}
