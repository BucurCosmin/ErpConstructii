import { DeleteOutlined, FilePdfOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, DatePicker, Form, Input, InputNumber, Progress, Row, Select, Space, Statistic, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useProiecte } from '../../api/interogari'
import { useBonuriConsum, useConsuma, useConsumPeArticole, useDevizReferinta, useSoldStoc } from '../../api/stocuri'
import type { BonConsum, ConsumPeArticole } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'
import { deschidePdf } from '../../util/pdf'

const GENERALE = '__generale__'
const bani = (v: number | null) => (v === null ? '—' : formatLei(v))

interface Valori {
  proiectId?: string
  data: Dayjs
  observatii?: string
  linii: { articolId?: string; cantitate?: number | null; articolDeviz?: string }[]
}

/**
 * Bon de consum: materialele din „Materiale în șantier” ale proiectului se consumă pe articolele devizului (la CMP).
 * Consumul fără articol merge la „cheltuieli generale de șantier”.
 */
export function ConsumSantier({ poateCrea }: { poateCrea: boolean }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<Valori>()
  const proiecte = useProiecte()
  const consuma = useConsuma()
  const bonuri = useBonuriConsum()
  const proiectId = Form.useWatch('proiectId', form) as string | undefined
  const linii = Form.useWatch('linii', form) as Valori['linii'] | undefined
  const sold = useSoldStoc({ tip: 'Santier', proiectId })
  const deviz = useDevizReferinta(proiectId)
  const disponibil = useMemo(() => new Map((proiectId ? sold.data ?? [] : []).map((s) => [s.articolId, s])), [sold.data, proiectId])

  const optiuniDeviz = [
    { value: GENERALE, label: 'Cheltuieli generale de șantier (fără articol)' },
    ...(deviz.data?.articole ?? []).map((a) => ({ value: a.cheie, label: `${a.simbol} — ${a.denumire} (${formatCantitate(a.cantitate)} ${a.unitateMasura})` })),
  ]

  const trimite = async (v: Valori) => {
    if (!v.proiectId) return
    try {
      const bc = await consuma.mutateAsync({
        proiectId: v.proiectId,
        data: v.data.format('YYYY-MM-DD'),
        observatii: v.observatii?.trim() || null,
        linii: v.linii.map((l) => ({
          articolId: l.articolId!,
          cantitate: Number(l.cantitate),
          articolDevizCheie: !l.articolDeviz || l.articolDeviz === GENERALE ? null : l.articolDeviz,
        })),
      })
      message.success(`${bc.numar}: consumul a fost înregistrat pe articolele devizului.`)
      form.setFieldsValue({ linii: [{}], observatii: undefined })
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="large">
      <Card title="Bon de consum">
        <Form<Valori> form={form} name="consum" layout="vertical" onFinish={trimite} disabled={!poateCrea} initialValues={{ data: dayjs(), linii: [{}] }}>
          <Row gutter={12}>
            <Col xs={24} md={10}>
              <Form.Item name="proiectId" label="Proiect (șantier)" rules={[{ required: true, message: 'Alege proiectul.' }]}>
                <Select showSearch={{ optionFilterProp: 'label' }} options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="data" label="Data" rules={[{ required: true }]}>
                <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
              </Form.Item>
            </Col>
            <Col xs={12} md={10}>
              <Form.Item name="observatii" label="Observații">
                <Input maxLength={1000} />
              </Form.Item>
            </Col>
          </Row>
          {proiectId && deviz.isError && (
            <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="Proiectul nu are încă un deviz: consumul se poate înregistra doar pe cheltuieli generale." />
          )}
          {proiectId && deviz.data && (
            <Typography.Paragraph type="secondary">
              Deviz de referință: {deviz.data.denumire} ({deviz.data.stare === 'Aprobata' ? 'aprobat' : 'ciornă'}, {deviz.data.articole.length} articole)
            </Typography.Paragraph>
          )}
          {proiectId && sold.data?.length === 0 && (
            <Alert type="info" showIcon style={{ marginBottom: 12 }} title="Nu sunt materiale în șantierul acestui proiect. Transferă-le întâi din depozit." />
          )}

          <Form.List name="linii">
            {(campuri, { add, remove }) => (
              <>
                {campuri.map(({ key, name }) => {
                  const s = linii?.[name]?.articolId ? disponibil.get(linii[name].articolId!) : undefined
                  return (
                    <Row key={key} gutter={8} align="top">
                      <Col flex="1 1 320px">
                        <Form.Item name={[name, 'articolId']} rules={[{ required: true, message: 'Alege materialul.' }]}>
                          <Select showSearch={{ optionFilterProp: 'label' }} placeholder="Material din șantier"
                            options={[...disponibil.values()].map((x) => ({
                              value: x.articolId,
                              label: `${x.articolCod} — ${x.denumire} · în șantier ${formatCantitate(x.cantitate)} ${x.unitateMasura}`,
                            }))} />
                        </Form.Item>
                      </Col>
                      <Col flex="180px">
                        <Form.Item name={[name, 'cantitate']}
                          rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, message: '> 0' }]}>
                          <InputNumber min={0} decimalSeparator="," style={{ width: '100%' }} addonAfter={s?.unitateMasura} />
                        </Form.Item>
                      </Col>
                      <Col flex="1 1 380px">
                        <Form.Item name={[name, 'articolDeviz']} rules={[{ required: true, message: 'Pe ce articol de deviz?' }]}>
                          <Select showSearch={{ optionFilterProp: 'label' }} placeholder="Articol de deviz" options={optiuniDeviz} />
                        </Form.Item>
                      </Col>
                      <Col flex="32px">
                        <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1} />
                      </Col>
                    </Row>
                  )
                })}
                <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} block style={{ marginBottom: 16 }}>Adaugă material</Button>
              </>
            )}
          </Form.List>
          <Button type="primary" htmlType="submit" loading={consuma.isPending}>Emite bonul de consum</Button>
        </Form>
      </Card>

      <Card title="Bonuri de consum">
        <Table<BonConsum>
          rowKey="id"
          size="small"
          loading={bonuri.isLoading}
          dataSource={bonuri.data}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          expandable={{
            expandedRowRender: (b) => (
              <Table size="small" pagination={false} rowKey={(l) => l.articolCod + (l.articolDevizCheie ?? '')} dataSource={b.linii}
                columns={[
                  { title: 'Material', render: (_, l) => `${l.articolCod} — ${l.denumire}` },
                  { title: 'Cantitate', width: 130, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
                  { title: 'Articol de deviz', render: (_, l) => (l.articolDevizSimbol ? `${l.articolDevizSimbol} — ${l.articolDevizDenumire}` : <Tag>cheltuieli generale</Tag>) },
                  { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right', render: bani },
                ]} />
            ),
          }}
          columns={[
            { title: 'Număr', dataIndex: 'numar', width: 140 },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 140 },
            { title: 'Linii', width: 80, align: 'right', render: (_, b) => b.linii.length },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
            { title: 'Emis de', dataIndex: 'emisDe' },
            {
              title: '', width: 80,
              render: (_, b) => <Button size="small" icon={<FilePdfOutlined />} onClick={() => void deschidePdf(`/api/stocuri/consumuri/${b.id}/pdf`).catch((x) => message.error((x as Error).message))}>PDF</Button>,
            },
          ]}
        />
      </Card>
    </Space>
  )
}

/** Consumul real de materiale pe articolele devizului, față de bugetul de materiale al fiecărui articol. */
export function ConsumPeArticoleRaport() {
  const proiecte = useProiecte()
  const [proiectId, setProiectId] = useState<string>()
  const raport = useConsumPeArticole(proiectId)
  const r: ConsumPeArticole | undefined = raport.data
  const cuValori = r?.totalConsumat !== null && r?.totalConsumat !== undefined

  return (
    <Card title="Consum real de materiale pe articolele devizului">
      <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Alege proiectul" style={{ width: 420, marginBottom: 16, display: 'block' }} value={proiectId}
        onChange={setProiectId} options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
      {r && (
        <>
          {!cuValori && <Alert type="info" showIcon style={{ marginBottom: 12 }} title="Valorile se văd doar cu dreptul „prețuri de cost”; mai jos sunt cantitățile consumate." />}
          {cuValori && (
            <Space size="large" style={{ marginBottom: 12 }}>
              <Statistic title="Buget materiale (deviz)" value={r.totalBuget ?? 0} formatter={(v) => formatLei(Number(v))} />
              <Statistic title="Consumat" value={r.totalConsumat ?? 0} formatter={(v) => formatLei(Number(v))} />
              <Typography.Text type="secondary">{r.deviz ? `Deviz de referință: ${r.deviz}` : 'Proiectul nu are deviz.'}</Typography.Text>
            </Space>
          )}
          <Table
            rowKey={(a) => a.cheie ?? 'generale'}
            size="small"
            loading={raport.isLoading}
            dataSource={r.articole}
            pagination={{ pageSize: 30, hideOnSinglePage: true }}
            expandable={{
              rowExpandable: (a) => a.materiale.length > 0,
              expandedRowRender: (a) => (
                <Table size="small" pagination={false} rowKey="cod" dataSource={a.materiale}
                  columns={[
                    { title: 'Material', render: (_, m) => `${m.cod} — ${m.denumire}` },
                    { title: 'Cantitate', width: 140, align: 'right', render: (_, m) => `${formatCantitate(m.cantitate)} ${m.unitateMasura}` },
                    { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
                  ]} />
              ),
            }}
            columns={[
              { title: 'Simbol', dataIndex: 'simbol', width: 110 },
              { title: 'Articol', dataIndex: 'denumire', ellipsis: true },
              { title: 'Buget material', dataIndex: 'bugetMaterial', width: 140, align: 'right', render: bani },
              { title: 'Consumat', dataIndex: 'consumat', width: 130, align: 'right', render: bani },
              {
                title: 'Diferență', dataIndex: 'diferenta', width: 130, align: 'right',
                render: (v: number | null) => (v === null ? '—' : <Typography.Text type={v > 0 ? 'danger' : undefined}>{formatLei(v)}</Typography.Text>),
              },
              {
                title: 'Consumat din buget', dataIndex: 'procentConsumat', width: 180,
                render: (p: number | null) => (p === null ? '—' : <Progress percent={Math.min(p, 100)} format={() => `${p}%`} status={p > 100 ? 'exception' : 'normal'} size="small" />),
              },
            ]}
          />
        </>
      )}
    </Card>
  )
}
