import { DeleteOutlined, FileDoneOutlined, FilePdfOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, DatePicker, Form, Input, Modal, Row, Segmented, Select, Space, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useProiecte } from '../../api/interogari'
import { useGestiuni, useReturneaza, useSoldStoc, useTransfera, useTransferuri } from '../../api/stocuri'
import type { FacturaPentruConsum, TransferStoc } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'
import { deschidePdf } from '../../util/pdf'
import { NumarRo } from '../../componente/NumarRo'
import { TabelFacturiReceptionate } from './TabelFacturiReceptionate'

interface Valori {
  gestiuneSursaId?: string
  proiectId?: string
  data: Dayjs
  observatii?: string
  linii: { articolId?: string; cantitate?: number | null }[]
}

/** Bon de transfer: materiale din depozit în „Materiale în șantier” ale unui proiect sau, la retur, înapoi în depozit; evaluate la CMP. */
export function TransferSantier({ poateCrea }: { poateCrea: boolean }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<Valori>()
  const gestiuni = useGestiuni()
  const proiecte = useProiecte()
  const transfera = useTransfera()
  const returneaza = useReturneaza()
  const transferuri = useTransferuri()
  const [retur, setRetur] = useState(false)
  const sursaId = Form.useWatch('gestiuneSursaId', form) as string | undefined
  const proiectAles = Form.useWatch('proiectId', form) as string | undefined
  const linii = Form.useWatch('linii', form) as Valori['linii'] | undefined
  // Spre șantier: materialele din depozit; la retur: materialele din șantierul proiectului.
  const sold = useSoldStoc(retur ? { tip: 'Santier', proiectId: proiectAles ?? '00000000-0000-0000-0000-000000000000' } : { gestiuneId: sursaId })

  const depozite = useMemo(() => (gestiuni.data ?? []).filter((g) => g.tip === 'Depozit'), [gestiuni.data])
  // Depozitul implicit, după ce lista se încarcă (pagina se poate deschide direct pe acest tab).
  useEffect(() => {
    if (depozite.length && !form.getFieldValue('gestiuneSursaId')) form.setFieldValue('gestiuneSursaId', depozite[0].id)
  }, [depozite, form])

  const disponibil = useMemo(() => new Map((sold.data ?? []).map((s) => [s.articolId, s])), [sold.data])

  const [dinFactura, setDinFactura] = useState(false)
  const [facturaAleasa, setFacturaAleasa] = useState<FacturaPentruConsum>()
  const [seTransfera, setSeTransfera] = useState(false)
  const inchideFactura = () => { setDinFactura(false); setFacturaAleasa(undefined) }
  // Liniile facturii vin pe (material, depozitul recepției): fiecare trebuie să fie întreagă în depozitul ei.
  const liniiFactura = facturaAleasa?.linii ?? []
  const lipsesc = liniiFactura.filter((l) => l.inDepozit < l.cantitate)
  const dejaInSantier = liniiFactura.length > 0 && liniiFactura.every((l) => l.inSantier >= l.cantitate)

  /** Toată factura în șantier: un bon de transfer pe fiecare depozit în care a fost recepționată, cu cantitățile din NIR. */
  const transferaFactura = async () => {
    const proiectId = form.getFieldValue('proiectId') as string | undefined
    if (!proiectId || !facturaAleasa || lipsesc.length > 0) return
    const data = ((form.getFieldValue('data') as Dayjs | undefined) ?? dayjs()).format('YYYY-MM-DD')
    const peDepozit = new Map<string, { articolId: string; cantitate: number }[]>()
    for (const l of liniiFactura) peDepozit.set(l.depozitId, [...(peDepozit.get(l.depozitId) ?? []), { articolId: l.articolId, cantitate: l.cantitate }])
    setSeTransfera(true)
    try {
      for (const [gestiuneSursaId, linii] of peDepozit) {
        const bt = await transfera.mutateAsync({
          proiectId, gestiuneSursaId, data, linii,
          observatii: (form.getFieldValue('observatii') as string | undefined)?.trim() || `Factura ${facturaAleasa.numar} (${facturaAleasa.furnizorDenumire})`,
        })
        message.success(`${bt.numar}: factura ${facturaAleasa.numar} a fost transferată în ${bt.gestiuneDestinatie}.`)
      }
      form.setFieldsValue({ linii: [{}], observatii: undefined })
      inchideFactura()
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeTransfera(false)
    }
  }

  const trimite = async (v: Valori) => {
    if (!v.gestiuneSursaId || !v.proiectId) return
    try {
      const comun = {
        proiectId: v.proiectId,
        data: v.data.format('YYYY-MM-DD'),
        observatii: v.observatii?.trim() || null,
        linii: v.linii.map((l) => ({ articolId: l.articolId!, cantitate: Number(l.cantitate) })),
      }
      const bt = retur
        ? await returneaza.mutateAsync({ ...comun, gestiuneDestinatieId: v.gestiuneSursaId })
        : await transfera.mutateAsync({ ...comun, gestiuneSursaId: v.gestiuneSursaId })
      message.success(`${bt.numar}: materialele au ajuns în ${bt.gestiuneDestinatie}.`)
      form.setFieldsValue({ linii: [{}], observatii: undefined })
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="large">
      <Card
        title={retur ? 'Retur în depozit (bon de transfer)' : 'Transfer în șantier (bon de transfer)'}
        extra={<Segmented value={retur ? 'retur' : 'santier'} onChange={(v) => { setRetur(v === 'retur'); form.setFieldsValue({ linii: [{}] }) }}
          options={[{ value: 'santier', label: 'Depozit → șantier' }, { value: 'retur', label: 'Retur șantier → depozit' }]} />}
      >
        <Form<Valori> form={form} name="transfer" layout="vertical" onFinish={trimite} disabled={!poateCrea}
          initialValues={{ data: dayjs(), linii: [{}], gestiuneSursaId: depozite[0]?.id }}>
          <Row gutter={12}>
            <Col xs={24} md={7}>
              <Form.Item name="gestiuneSursaId" label={retur ? 'În depozitul' : 'Din depozitul'} rules={[{ required: true, message: 'Alege depozitul.' }]}>
                <Select options={depozite.map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} loading={gestiuni.isLoading} />
              </Form.Item>
            </Col>
            <Col xs={24} md={9}>
              <Form.Item name="proiectId" label={retur ? 'Din șantierul proiectului' : 'În șantierul proiectului'} rules={[{ required: true, message: 'Alege proiectul.' }]}>
                <Select showSearch={{ optionFilterProp: 'label' }} options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="data" label="Data" rules={[{ required: true }]}>
                <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="observatii" label="Observații">
                <Input maxLength={1000} />
              </Form.Item>
            </Col>
          </Row>

          {(retur ? proiectAles : sursaId) && sold.data?.length === 0 && (
            <Alert type="warning" showIcon title={retur ? 'Șantierul proiectului nu are materiale.' : 'Depozitul nu are materiale în stoc.'} style={{ marginBottom: 12 }} />
          )}
          <Form.List name="linii">
            {(campuri, { add, remove }) => (
              <>
                {campuri.map(({ key, name }) => {
                  const s = linii?.[name]?.articolId ? disponibil.get(linii[name].articolId!) : undefined
                  return (
                    <Row key={key} gutter={8} align="top">
                      <Col flex="auto">
                        <Form.Item name={[name, 'articolId']} rules={[{ required: true, message: 'Alege materialul.' }]}>
                          <Select
                            showSearch={{ optionFilterProp: 'label' }}
                            placeholder={retur ? 'Material din șantier' : 'Material din depozit'}
                            options={(sold.data ?? []).map((x) => ({
                              value: x.articolId,
                              label: `${x.articolCod} — ${x.denumire} · disponibil ${formatCantitate(x.cantitate)} ${x.unitateMasura}`,
                            }))}
                          />
                        </Form.Item>
                      </Col>
                      <Col flex="200px">
                        <Form.Item name={[name, 'cantitate']}
                          rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, max: s?.cantitate, message: s ? `Max. ${formatCantitate(s.cantitate)}` : '> 0' }]}>
                          <NumarRo min={0} style={{ width: '100%' }} addonAfter={s?.unitateMasura} />
                        </Form.Item>
                      </Col>
                      <Col flex="32px">
                        <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1} />
                      </Col>
                    </Row>
                  )
                })}
                {retur
                  ? <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} block style={{ marginBottom: 16 }}>Adaugă material</Button>
                  : (
                    <Space.Compact block style={{ marginBottom: 16 }}>
                      <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} style={{ width: '60%' }}>Adaugă material</Button>
                      <Button type="dashed" icon={<FileDoneOutlined />} onClick={() => setDinFactura(true)} disabled={!proiectAles} style={{ width: '40%' }}
                        title={proiectAles ? undefined : 'Alege întâi proiectul'}>
                        Transferă o factură întreagă
                      </Button>
                    </Space.Compact>
                  )}
              </>
            )}
          </Form.List>
          <Typography.Text type="secondary" style={{ marginRight: 16 }}>
            Materialele pleacă la costul mediu (CMP) din depozit; gestiunea de șantier a proiectului se creează la primul transfer.
          </Typography.Text>
          <Button type="primary" htmlType="submit" loading={transfera.isPending || returneaza.isPending}>{retur ? 'Emite returul' : 'Emite bonul de transfer'}</Button>
        </Form>
      </Card>

      <Modal
        open={dinFactura}
        onCancel={inchideFactura}
        width="min(1100px, 96vw)"
        title="Transferă o factură întreagă în șantier"
        okText="Transferă factura în șantier"
        okButtonProps={{ disabled: !facturaAleasa || lipsesc.length > 0, loading: seTransfera }}
        onOk={() => void transferaFactura()}
        destroyOnHidden
      >
        <TabelFacturiReceptionate proiectId={dinFactura ? proiectAles : undefined} aleasa={facturaAleasa} onAlege={setFacturaAleasa} />
        {facturaAleasa && (
          <>
            <Typography.Title level={5} style={{ marginTop: 12 }}>Materialele facturii {facturaAleasa.numar}</Typography.Title>
            {!facturaAleasa.acelasiProiect && (
              <Alert type="info" showIcon style={{ marginBottom: 8 }} title={`Factura e înregistrată pe proiectul ${facturaAleasa.proiectCod}, nu pe cel ales pentru transfer.`} />
            )}
            {lipsesc.length > 0
              ? <Alert type="error" showIcon style={{ marginBottom: 8 }} title="Nu toate materialele facturii sunt în depozit: factura nu se poate transfera întreagă."
                  description="Transferă materialele disponibile cu „Adaugă material”, în cantitățile care există." />
              : dejaInSantier && (
                <Alert type="warning" showIcon style={{ marginBottom: 8 }} title="În șantier sunt deja cel puțin cantitățile din factură."
                  description="Poate că factura a fost transferată deja: verifică bonurile de transfer de mai jos înainte să o transferi din nou." />
              )}
            <Table
              size="small"
              rowKey={(l) => `${l.articolId}-${l.depozitId}`}
              pagination={false}
              dataSource={liniiFactura}
              columns={[
                { title: 'Material', render: (_, l) => `${l.articolCod} — ${l.denumire}` },
                { title: 'Din factură (NIR)', width: 140, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
                { title: 'Din depozitul', dataIndex: 'depozit', width: 170 },
                {
                  title: 'În depozit', width: 150,
                  render: (_, l) => l.inDepozit >= l.cantitate
                    ? <Tag color="green">{formatCantitate(l.inDepozit)} {l.unitateMasura}</Tag>
                    : <Tag color="red">{formatCantitate(l.inDepozit)} — lipsesc {formatCantitate(l.cantitate - l.inDepozit)}</Tag>,
                },
                { title: 'Acum în șantier', width: 120, align: 'right', render: (_, l) => formatCantitate(l.inSantier) },
              ]}
            />
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              Se emite un bon de transfer cu data din formular, din depozitul în care a fost recepționată factura, la CMP.
            </Typography.Paragraph>
          </>
        )}
      </Modal>

      <Card title="Bonuri de transfer">
        <Table<TransferStoc>
          rowKey="id"
          size="small"
          loading={transferuri.isLoading}
          dataSource={transferuri.data}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          expandable={{
            expandedRowRender: (t) => (
              <Table size="small" pagination={false} rowKey="articolCod" dataSource={t.linii}
                columns={[
                  { title: 'Cod', dataIndex: 'articolCod', width: 110 },
                  { title: 'Material', dataIndex: 'denumire' },
                  { title: 'Cantitate', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}`, width: 140, align: 'right' },
                  { title: 'CMP', dataIndex: 'pretUnitar', width: 110, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
                  { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
                ]} />
            ),
          }}
          columns={[
            { title: 'Număr', dataIndex: 'numar', width: 140 },
            { title: '', dataIndex: 'directie', width: 90, render: (d: TransferStoc['directie']) => (d === 'Retur' ? <Tag color="orange">retur</Tag> : <Tag color="blue">în șantier</Tag>) },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Din', dataIndex: 'gestiuneSursa' },
            { title: 'În', dataIndex: 'gestiuneDestinatie' },
            { title: 'Materiale', width: 100, align: 'right', render: (_, t) => t.linii.length },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
            { title: 'Emis de', dataIndex: 'emisDe', width: 160 },
            {
              title: '', width: 80,
              render: (_, t) => <Button size="small" icon={<FilePdfOutlined />} onClick={() => void deschidePdf(`/api/stocuri/transferuri/${t.id}/pdf`).catch((x) => message.error((x as Error).message))}>PDF</Button>,
            },
          ]}
        />
      </Card>
    </Space>
  )
}
