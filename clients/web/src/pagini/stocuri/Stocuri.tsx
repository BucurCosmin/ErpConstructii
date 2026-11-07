import { DownloadOutlined, EditOutlined, EyeOutlined, FilePdfOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Drawer, Flex, Form, Input, Modal, Row, Select, Space, Statistic, Switch, Table, Tabs, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import {
  useArticoleStoc,
  useCreeazaDepozit,
  useFacturiDeReceptionat,
  useFisaMagazie,
  useGestiuni,
  useModificaArticol,
  usePropunereReceptie,
  useSoldStoc,
  useTrasabilitate,
} from '../../api/stocuri'
import type { ArticolStoc, FacturaDeReceptionat, FiltreReceptii, LotStoc, MiscareStoc, SoldStoc } from '../../api/stocuri'
import { deschidePdf } from '../../util/pdf'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatCantitate, formatLei } from '../../util/format'
import { PrevizualizareFactura } from './PrevizualizareFactura'
import { exportaCsv, ProvenientaStoc } from './ProvenientaStoc'
import { RegistruNir } from './RegistruNir'
import { ReceptieFactura, type ModReceptie } from './ReceptieFactura'
import { ConsumPeArticoleRaport, ConsumSantier } from './ConsumSantier'
import { InventarGestiune } from './InventarGestiune'
import { TransferSantier } from './TransferSantier'

const bani = (v: number | null) => (v === null ? '—' : formatLei(v))
const tipMiscare: Record<MiscareStoc['tip'], { text: string; culoare: string }> = {
  IesireConsum: { text: 'Consum (bon)', culoare: 'red' },
  IntrareNir: { text: 'Intrare (NIR)', culoare: 'green' },
  IesireTransfer: { text: 'Ieșire (transfer)', culoare: 'orange' },
  IntrareTransfer: { text: 'Intrare (transfer)', culoare: 'blue' },
  IesireStornoNir: { text: 'Storno NIR', culoare: 'volcano' },
  PlusInventar: { text: 'Plus inventar', culoare: 'cyan' },
  MinusInventar: { text: 'Minus inventar', culoare: 'magenta' },
}

/** Loturile unui material (factura → NIR de origine), la prețul de intrare. */
function TabelLoturi({ loturi }: { loturi: LotStoc[] }) {
  const cuPreturi = loturi.some((l) => l.valoare !== null)
  return (
    <Table<LotStoc>
      size="small"
      pagination={false}
      rowKey={(l) => `${l.documentId}-${l.dataIntrare}-${l.cantitate}`}
      dataSource={loturi}
      locale={{ emptyText: 'Fără proveniență (material intrat fără NIR).' }}
      columns={[
        {
          title: 'Factura', width: 170,
          render: (_, l) => l.facturaNumar
            ? <>{l.facturaNumar}<div><Typography.Text type="secondary">{dayjs(l.facturaData).format('DD.MM.YYYY')}</Typography.Text></div></>
            : <Tag color={l.origine === 'Inventar' ? 'purple' : 'default'}>{l.origine === 'Inventar' ? 'plus inventar' : 'transfer'}</Tag>,
        },
        { title: 'Furnizor', dataIndex: 'furnizorDenumire' },
        { title: 'NIR / intrat la', width: 160, render: (_, l) => <>{l.documentNumar}<div><Typography.Text type="secondary">{dayjs(l.dataIntrare).format('DD.MM.YYYY')}</Typography.Text></div></> },
        { title: 'Proiect factură', dataIndex: 'facturaProiectCod', width: 120 },
        { title: 'Cantitate', width: 120, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
        ...(cuPreturi ? [
          { title: 'Preț intrare', dataIndex: 'pretIntrare', width: 110, align: 'right' as const, render: bani },
          { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right' as const, render: bani },
        ] : []),
      ]}
    />
  )
}

/**
 * Tabelul de sold: cantitate, CMP, valoare și proveniența (factura → NIR) a fiecărui material; rândul se desface cu
 * loturile, clic pe rând → fișa de magazie. Raportul (PDF sumar / pe proveniență, CSV) folosește aceleași filtre.
 */
function TabelSold({ randuri, incarcare, cuGestiune, filtre }: {
  randuri: SoldStoc[] | undefined; incarcare: boolean; cuGestiune: boolean
  filtre: { tip: 'Depozit' | 'Santier'; gestiuneId?: string; proiectId?: string; material?: string }
}) {
  const { message } = App.useApp()
  const [fisa, setFisa] = useState<SoldStoc>()
  const miscari = useFisaMagazie(fisa?.gestiuneId, fisa?.articolId)
  const loturi = useTrasabilitate({ tip: filtre.tip, gestiuneId: filtre.gestiuneId, material: filtre.material })
  const peMaterial = useMemo(() => {
    const m = new Map<string, LotStoc[]>()
    for (const l of loturi.data ?? []) {
      const k = `${l.gestiuneId}-${l.articolId}`
      m.set(k, [...(m.get(k) ?? []), l])
    }
    return m
  }, [loturi.data])
  const total = (randuri ?? []).reduce((s, r) => s + (r.valoare ?? 0), 0)
  const cuValori = (randuri ?? []).some((r) => r.valoare !== null)
  const cheie = (r: SoldStoc) => `${r.gestiuneId}-${r.articolId}`
  const loturiAfisate = () => (randuri ?? []).flatMap((r) => peMaterial.get(cheie(r)) ?? [])

  const raportPdf = (detaliat: boolean) => {
    const p = new URLSearchParams({ tip: filtre.tip, detaliat: String(detaliat) })
    if (filtre.gestiuneId) p.set('gestiuneId', filtre.gestiuneId)
    if (filtre.proiectId) p.set('proiectId', filtre.proiectId)
    if (filtre.material) p.set('material', filtre.material)
    void deschidePdf(`/api/stocuri/raport-stoc/pdf?${p.toString()}`).catch((e) => message.error((e as Error).message))
  }

  return (
    <>
      <Flex justify="space-between" align="end" wrap gap={8} style={{ marginBottom: 12 }}>
        {cuValori ? <Statistic title="Valoarea stocului (CMP)" value={total} formatter={(v) => formatLei(Number(v))} /> : <span />}
        <Space wrap>
          <Button icon={<FilePdfOutlined />} disabled={!randuri?.length} onClick={() => raportPdf(false)}>Raport stoc (PDF)</Button>
          <Button icon={<FilePdfOutlined />} disabled={!randuri?.length} onClick={() => raportPdf(true)}>Raport pe factură / NIR (PDF)</Button>
          <Button icon={<DownloadOutlined />} disabled={!randuri?.length || loturi.isLoading} onClick={() => exportaCsv(loturiAfisate())}>Excel (CSV)</Button>
        </Space>
      </Flex>
      <Table<SoldStoc>
        rowKey={cheie}
        size="small"
        loading={incarcare}
        dataSource={randuri}
        pagination={{ pageSize: 25, hideOnSinglePage: true }}
        onRow={(r) => ({ onClick: () => setFisa(r), style: { cursor: 'pointer' } })}
        expandable={{
          expandedRowRender: (r) => <TabelLoturi loturi={peMaterial.get(cheie(r)) ?? []} />,
          rowExpandable: () => !loturi.isLoading,
        }}
        columns={[
          ...(cuGestiune ? [{ title: 'Gestiune', dataIndex: 'gestiuneDenumire', width: 220, ellipsis: true }] : []),
          { title: 'Cod', dataIndex: 'articolCod', width: 110 },
          { title: 'Material', dataIndex: 'denumire' },
          {
            title: 'Factura → NIR', width: 230,
            render: (_, r) => {
              const l = peMaterial.get(cheie(r)) ?? []
              if (loturi.isLoading) return <Typography.Text type="secondary">…</Typography.Text>
              const facturi = [...new Set(l.map((x) => x.facturaNumar ?? (x.origine === 'Inventar' ? 'plus inventar' : 'transfer')))]
              if (facturi.length === 0) return null
              return (
                <Tooltip title={l.map((x, i) => <div key={i}>{formatCantitate(x.cantitate)} {x.unitateMasura}: {x.facturaNumar ?? '—'} → {x.documentNumar}</div>)}>
                  <span>{facturi[0]} → {l[0].documentNumar}{facturi.length > 1 && <Typography.Text type="secondary"> +{facturi.length - 1}</Typography.Text>}</span>
                </Tooltip>
              )
            },
          },
          { title: 'Cantitate', width: 140, align: 'right', render: (_, r) => `${formatCantitate(r.cantitate)} ${r.unitateMasura}` },
          { title: 'Preț mediu (CMP)', dataIndex: 'cmp', width: 140, align: 'right', render: bani },
          { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
        ]}
      />
      <Drawer open={!!fisa} onClose={() => setFisa(undefined)} size={820} title={fisa && `Fișa de magazie — ${fisa.articolCod} ${fisa.denumire}`}>
        {fisa && (
          <>
            <Typography.Paragraph type="secondary">{fisa.gestiuneDenumire} · în stoc {formatCantitate(fisa.cantitate)} {fisa.unitateMasura}</Typography.Paragraph>
            <Typography.Title level={5}>Proveniența stocului (factura → NIR)</Typography.Title>
            <TabelLoturi loturi={peMaterial.get(cheie(fisa)) ?? []} />
            <Typography.Title level={5} style={{ marginTop: 16 }}>Mișcări</Typography.Title>
            <Table<MiscareStoc>
              rowKey="id"
              size="small"
              pagination={false}
              loading={miscari.isLoading}
              dataSource={miscari.data}
              columns={[
                { title: 'Data', dataIndex: 'data', width: 100, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
                { title: 'Document', dataIndex: 'documentNumar', width: 130 },
                { title: 'Tip', dataIndex: 'tip', render: (t: MiscareStoc['tip']) => <Tag color={tipMiscare[t].culoare}>{tipMiscare[t].text}</Tag> },
                { title: 'Cantitate', dataIndex: 'cantitate', align: 'right', width: 110, render: (v: number) => formatCantitate(v) },
                { title: 'Valoare', dataIndex: 'valoare', align: 'right', width: 120, render: bani },
              ]}
            />
          </>
        )}
      </Drawer>
    </>
  )
}

function StocDepozite({ poateEdita }: { poateEdita: boolean }) {
  const { message } = App.useApp()
  const gestiuni = useGestiuni()
  const [gestiuneId, setGestiuneId] = useState<string>()
  const [cauta, setCauta] = useState<string>()
  const sold = useSoldStoc({ tip: 'Depozit', gestiuneId, cauta })
  const creeaza = useCreeazaDepozit()
  const [depozitNou, setDepozitNou] = useState(false)
  const [form] = Form.useForm<{ cod: string; denumire: string }>()

  return (
    <Card
      title="Stoc în depozite"
      extra={poateEdita && <Button icon={<PlusOutlined />} onClick={() => setDepozitNou(true)}>Depozit nou</Button>}
    >
      <Space wrap style={{ marginBottom: 12 }}>
        <Select allowClear placeholder="Toate depozitele" style={{ width: 280 }} value={gestiuneId} onChange={setGestiuneId}
          options={(gestiuni.data ?? []).filter((g) => g.tip === 'Depozit').map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} />
        <Input.Search allowClear placeholder="Caută material" style={{ width: 280 }} onSearch={(v) => setCauta(v || undefined)} />
      </Space>
      <TabelSold randuri={sold.data} incarcare={sold.isLoading} cuGestiune={!gestiuneId} filtre={{ tip: 'Depozit', gestiuneId, material: cauta }} />
      <Modal open={depozitNou} title="Depozit nou" okText="Adaugă" cancelText="Renunță" onCancel={() => setDepozitNou(false)} onOk={() => form.submit()}
        confirmLoading={creeaza.isPending} destroyOnHidden>
        <Form form={form} layout="vertical" name="depozitNou" onFinish={async (v) => {
          try {
            await creeaza.mutateAsync(v)
            message.success('Depozitul a fost adăugat.')
            setDepozitNou(false)
          } catch (e) {
            message.error((e as Error).message)
          }
        }}>
          <Form.Item name="cod" label="Cod" rules={[{ required: true, whitespace: true }]}><Input maxLength={20} placeholder="DEP-02" /></Form.Item>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true }]}><Input maxLength={200} placeholder="Depozit Constanța" /></Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}

function MaterialeInSantier() {
  const proiecte = useProiecte()
  const [proiectId, setProiectId] = useState<string>()
  const [cauta, setCauta] = useState<string>()
  const sold = useSoldStoc({ tip: 'Santier', proiectId, cauta })
  return (
    <Card title="Materiale în șantier">
      <Space wrap style={{ marginBottom: 12 }}>
        <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Toate proiectele" style={{ width: 360 }} value={proiectId} onChange={setProiectId}
          options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
        <Input.Search allowClear placeholder="Caută material" style={{ width: 280 }} onSearch={(v) => setCauta(v || undefined)} />
      </Space>
      {sold.data?.length === 0 && !sold.isLoading && (
        <Alert type="info" showIcon style={{ marginBottom: 12 }} title="Niciun material în șantier. Materialele ajung aici prin transfer din depozit." />
      )}
      <TabelSold randuri={sold.data} incarcare={sold.isLoading} cuGestiune={!proiectId} filtre={{ tip: 'Santier', proiectId, material: cauta }} />
    </Card>
  )
}

/** Liniile de stoc ale facturii, cu ce s-a primit deja și ce mai e de primit. */
function LiniiFacturaDeReceptionat({ facturaId }: { facturaId: string }) {
  const p = usePropunereReceptie(facturaId)
  return (
    <Table size="small" pagination={false} loading={p.isLoading} rowKey="indexFactura" dataSource={p.data?.linii}
      columns={[
        { title: 'Material pe factură', dataIndex: 'denumire' },
        { title: 'Facturat', width: 120, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
        { title: 'Preț', dataIndex: 'pretUnitar', width: 110, align: 'right', render: bani },
        { title: 'Recepționat', width: 120, align: 'right', render: (_, l) => (l.receptionat > 0 ? formatCantitate(l.receptionat) : '—') },
        {
          title: 'De primit', width: 150, align: 'right',
          render: (_, l) => l.inchisa
            ? <Tag color="green">recepționată</Tag>
            : <Typography.Text strong>{formatCantitate(l.ramas)} {l.unitateMasura}</Typography.Text>,
        },
        { title: 'Material în stoc (propus)', width: 280, render: (_, l) => l.articol ? `${l.articol.cod} — ${l.articol.denumire}` : <Typography.Text type="secondary">nou</Typography.Text> },
      ]} />
  )
}

/** Recepțiile: facturile de recepționat (NIR nou, total sau parțial) și registrul tuturor NIR-urilor. */
function Receptii({ poateCrea, poateAnula }: { poateCrea: boolean; poateAnula: boolean }) {
  const [parametri, setParametri] = useSearchParams()
  const sub = parametri.get('nir') ?? 'de-receptionat'
  return (
    <Tabs
      type="card"
      activeKey={sub}
      onChange={(k) => setParametri((p) => { p.set('nir', k); return p }, { replace: true })}
      items={[
        { key: 'de-receptionat', label: 'De recepționat', children: <DeReceptionat poateCrea={poateCrea} /> },
        { key: 'registru', label: 'Registru NIR (toate)', children: <RegistruNir poateAnula={poateAnula} /> },
        { key: 'provenienta', label: 'Proveniență materiale (NIR / factură)', children: <ProvenientaStoc /> },
      ]}
    />
  )
}

function DeReceptionat({ poateCrea }: { poateCrea: boolean }) {
  const [filtre, setFiltre] = useState<FiltreReceptii>({})
  const deFacut = useFacturiDeReceptionat(filtre)
  const filtrat = !!(filtre.cauta || filtre.material)
  const gasite = (m?: string[] | null) => (m?.length ?? 0) > 0 && (
    <div style={{ marginTop: 2 }}>
      {m!.slice(0, 3).map((x) => <Tag key={x} color="gold" style={{ marginBottom: 2 }}>{x}</Tag>)}
      {m!.length > 3 && <Typography.Text type="secondary">+{m!.length - 3}</Typography.Text>}
    </div>
  )
  const [facturaId, setFacturaId] = useState<string>()
  const [mod, setMod] = useState<ModReceptie>('tot')
  const deschide = (id: string, m: ModReceptie) => { setMod(m); setFacturaId(id) }
  const [deVazut, setDeVazut] = useState<FacturaDeReceptionat>()
  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Form layout="inline" style={{ rowGap: 8 }}
        onFinish={(v: FiltreReceptii) => setFiltre({ cauta: v.cauta?.trim() || undefined, material: v.material?.trim() || undefined })}>
        <Form.Item name="material">
          <Input allowClear prefix={<SearchOutlined />} placeholder="Material (denumire sau cod)" style={{ width: 260 }} />
        </Form.Item>
        <Form.Item name="cauta">
          <Input allowClear placeholder="Factura sau furnizorul" style={{ width: 220 }} />
        </Form.Item>
        <Button type="primary" htmlType="submit">Filtrează</Button>
      </Form>
      <Card title={`Facturi de recepționat${deFacut.data ? ` (${deFacut.data.length})` : ''}`}>
        <Table<FacturaDeReceptionat>
          rowKey="facturaId"
          size="small"
          loading={deFacut.isLoading}
          dataSource={deFacut.data}
          locale={{ emptyText: filtrat ? 'Nicio factură de recepționat nu corespunde filtrului.' : 'Toate facturile cu materiale au fost recepționate.' }}
          expandable={{ expandedRowRender: (f) => <LiniiFacturaDeReceptionat facturaId={f.facturaId} />, expandRowByClick: true }}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          columns={[
            { title: 'Factura', dataIndex: 'numar', width: 150 },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Furnizor', render: (_, f) => <>{f.furnizorDenumire}{gasite(f.materialeGasite)}</> },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 130 },
            {
              title: 'Linii de stoc', width: 170, align: 'right',
              render: (_, f) => f.receptionataPartial
                ? <><Tag color="orange">parțial</Tag>{f.liniiDeschise} din {f.linii} de primit</>
                : f.linii,
            },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
            {
              title: '',
              width: 330,
              render: (_, f) => (
                <Space size={4} onClick={(e) => e.stopPropagation()}>
                  <Button size="small" icon={<EyeOutlined />} onClick={() => setDeVazut(f)}>Factura</Button>
                  <Tooltip title="Toate liniile, cu cantitățile din factură">
                    <Button type="primary" size="small" disabled={!poateCrea} onClick={() => deschide(f.facturaId, 'tot')}>Recepționează tot</Button>
                  </Tooltip>
                  <Tooltip title="Alegi liniile sosite și cantitățile; restul rămâne de recepționat">
                    <Button size="small" disabled={!poateCrea} onClick={() => deschide(f.facturaId, 'partial')}>Parțial</Button>
                  </Tooltip>
                </Space>
              ),
            },
          ]}
        />
      </Card>
      <ReceptieFactura facturaId={facturaId} mod={mod} onInchide={() => setFacturaId(undefined)} />
      <Drawer open={!!deVazut} onClose={() => setDeVazut(undefined)} size={1100} destroyOnHidden
        title={deVazut && `Factura ${deVazut.numar} din ${dayjs(deVazut.data).format('DD.MM.YYYY')} — ${deVazut.furnizorDenumire}`}
        extra={deVazut && (
          <Space>
            <Button type="primary" disabled={!poateCrea} onClick={() => { deschide(deVazut.facturaId, 'tot'); setDeVazut(undefined) }}>Recepționează tot</Button>
            <Button disabled={!poateCrea} onClick={() => { deschide(deVazut.facturaId, 'partial'); setDeVazut(undefined) }}>Parțial</Button>
          </Space>
        )}>
        {deVazut && (
          <Space orientation="vertical" style={{ width: '100%' }} size="middle">
            <LiniiFacturaDeReceptionat facturaId={deVazut.facturaId} />
            <PrevizualizareFactura facturaId={deVazut.facturaId} inaltime="70vh" />
          </Space>
        )}
      </Drawer>
    </Space>
  )
}

function Nomenclator({ poateEdita }: { poateEdita: boolean }) {
  const { message } = App.useApp()
  const articole = useArticoleStoc()
  const modifica = useModificaArticol()
  const [editat, setEditat] = useState<ArticolStoc>()
  const [cauta, setCauta] = useState('')
  const [form] = Form.useForm<{ denumire: string; unitateMasura: string; activ: boolean }>()
  const lista = (articole.data ?? []).filter((a) => !cauta || `${a.cod} ${a.denumire}`.toLowerCase().includes(cauta.toLowerCase()))
  return (
    <Card title="Nomenclator materiale" extra={<Input.Search allowClear placeholder="Caută" style={{ width: 260 }} onChange={(e) => setCauta(e.target.value)} />}>
      <Typography.Paragraph type="secondary">
        Materialele se creează la recepție, din textul facturii. Legătura dintre textul fiecărui furnizor și material se memorează, așa că
        la următoarea factură materialul e propus automat.
      </Typography.Paragraph>
      <Table<ArticolStoc>
        rowKey="id"
        size="small"
        loading={articole.isLoading}
        dataSource={lista}
        pagination={{ pageSize: 25, hideOnSinglePage: true }}
        columns={[
          { title: 'Cod', dataIndex: 'cod', width: 120 },
          { title: 'Denumire', dataIndex: 'denumire' },
          { title: 'UM', dataIndex: 'unitateMasura', width: 80 },
          { title: 'Stare', dataIndex: 'activ', width: 100, render: (a: boolean) => (a ? <Tag color="green">activ</Tag> : <Tag>inactiv</Tag>) },
          {
            title: '', width: 60,
            render: (_, a) => poateEdita && <Button type="text" icon={<EditOutlined />} onClick={() => { setEditat(a); form.setFieldsValue(a) }} />,
          },
        ]}
      />
      <Modal open={!!editat} title={editat && `Material ${editat.cod}`} okText="Salvează" cancelText="Renunță" onCancel={() => setEditat(undefined)}
        onOk={() => form.submit()} confirmLoading={modifica.isPending} destroyOnHidden>
        <Form form={form} layout="vertical" name="articol" onFinish={async (v) => {
          try {
            await modifica.mutateAsync({ id: editat!.id, ...v })
            message.success('Materialul a fost actualizat.')
            setEditat(undefined)
          } catch (e) {
            message.error((e as Error).message)
          }
        }}>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true }]}><Input maxLength={300} /></Form.Item>
          <Row gutter={12}>
            <Col span={12}><Form.Item name="unitateMasura" label="UM" rules={[{ required: true, whitespace: true }]}><Input maxLength={20} /></Form.Item></Col>
            <Col span={12}><Form.Item name="activ" label="Activ" valuePropName="checked"><Switch /></Form.Item></Col>
          </Row>
        </Form>
      </Modal>
    </Card>
  )
}

/** Stocurile: factură → recepție (NIR) în depozit → transfer în șantier; soldurile la cost mediu ponderat. */
export function Stocuri() {
  const permisiuni = usePermisiuni()
  const [parametri, setParametri] = useSearchParams()
  const poateCrea = permisiuni.data?.includes('aprovizionare.creare') ?? false
  const poateEdita = permisiuni.data?.includes('aprovizionare.editare') ?? false
  const poateAnula = permisiuni.data?.includes('aprovizionare.anulare') ?? false
  const poateAproba = permisiuni.data?.includes('aprovizionare.aprobare') ?? false
  const tab = parametri.get('tab') ?? 'depozite'

  return (
    <>
      <AntetPagina
        titlu="Stocuri"
        subtitlu="Materialele facturate intră în depozit după recepția gestionarului (NIR) și ajung în șantier prin bonuri de transfer, la cost mediu ponderat."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Stocuri' }]}
      />
      <Tabs
        activeKey={tab}
        onChange={(k) => setParametri({ tab: k })}
        items={[
          { key: 'depozite', label: 'Stoc în depozite', children: <StocDepozite poateEdita={poateEdita} /> },
          { key: 'santier', label: 'Materiale în șantier', children: <MaterialeInSantier /> },
          { key: 'receptii', label: 'Recepții (NIR)', children: <Receptii poateCrea={poateCrea} poateAnula={poateAnula} /> },
          { key: 'transfer', label: 'Transfer / retur', children: <TransferSantier poateCrea={poateCrea} /> },
          { key: 'consum', label: 'Consum în șantier', children: <ConsumSantier poateCrea={poateCrea} /> },
          { key: 'consum-articole', label: 'Consum pe articole', children: <ConsumPeArticoleRaport /> },
          { key: 'inventar', label: 'Inventar', children: <InventarGestiune poateEdita={poateEdita} poateAproba={poateAproba} /> },
          { key: 'nomenclator', label: 'Nomenclator materiale', children: <Nomenclator poateEdita={poateEdita} /> },
        ]}
      />
    </>
  )
}
