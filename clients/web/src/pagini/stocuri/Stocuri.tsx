import { EditOutlined, EyeOutlined, FilePdfOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Drawer, Form, Input, Modal, Row, Select, Space, Statistic, Switch, Table, Tabs, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import {
  useArticoleStoc,
  useCreeazaDepozit,
  useFacturiDeReceptionat,
  useFisaMagazie,
  useGestiuni,
  useModificaArticol,
  useNir,
  usePropunereReceptie,
  useSoldStoc,
  useStorneazaNir,
} from '../../api/stocuri'
import type { ArticolStoc, FacturaDeReceptionat, FiltreReceptii, MiscareStoc, Nir, SoldStoc } from '../../api/stocuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatCantitate, formatLei } from '../../util/format'
import { deschidePdf } from '../../util/pdf'
import { PrevizualizareFactura } from './PrevizualizareFactura'
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

/** Tabelul de sold: cantitate, CMP, valoare; clic pe rând → fișa de magazie. */
function TabelSold({ randuri, incarcare, cuGestiune }: { randuri: SoldStoc[] | undefined; incarcare: boolean; cuGestiune: boolean }) {
  const [fisa, setFisa] = useState<SoldStoc>()
  const miscari = useFisaMagazie(fisa?.gestiuneId, fisa?.articolId)
  const total = (randuri ?? []).reduce((s, r) => s + (r.valoare ?? 0), 0)
  const cuValori = (randuri ?? []).some((r) => r.valoare !== null)

  return (
    <>
      {cuValori && <Statistic title="Valoarea stocului" value={total} formatter={(v) => formatLei(Number(v))} style={{ marginBottom: 12 }} />}
      <Table<SoldStoc>
        rowKey={(r) => `${r.gestiuneId}-${r.articolId}`}
        size="small"
        loading={incarcare}
        dataSource={randuri}
        pagination={{ pageSize: 25, hideOnSinglePage: true }}
        onRow={(r) => ({ onClick: () => setFisa(r), style: { cursor: 'pointer' } })}
        columns={[
          ...(cuGestiune ? [{ title: 'Gestiune', dataIndex: 'gestiuneDenumire', width: 260, ellipsis: true }] : []),
          { title: 'Cod', dataIndex: 'articolCod', width: 110 },
          { title: 'Material', dataIndex: 'denumire' },
          { title: 'Cantitate', width: 150, align: 'right', render: (_, r) => `${formatCantitate(r.cantitate)} ${r.unitateMasura}` },
          { title: 'Preț mediu (CMP)', dataIndex: 'cmp', width: 150, align: 'right', render: bani },
          { title: 'Valoare', dataIndex: 'valoare', width: 140, align: 'right', render: bani },
        ]}
      />
      <Drawer open={!!fisa} onClose={() => setFisa(undefined)} size={720} title={fisa && `Fișa de magazie — ${fisa.articolCod} ${fisa.denumire}`}>
        {fisa && (
          <>
            <Typography.Paragraph type="secondary">{fisa.gestiuneDenumire}</Typography.Paragraph>
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
      <TabelSold randuri={sold.data} incarcare={sold.isLoading} cuGestiune={!gestiuneId} />
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
      <TabelSold randuri={sold.data} incarcare={sold.isLoading} cuGestiune={!proiectId} />
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

function Receptii({ poateCrea, poateAnula }: { poateCrea: boolean; poateAnula: boolean }) {
  const { message } = App.useApp()
  const [filtre, setFiltre] = useState<FiltreReceptii>({})
  const deFacut = useFacturiDeReceptionat(filtre)
  const nir = useNir(filtre)
  const filtrat = !!(filtre.cauta || filtre.material)
  const gasite = (m?: string[] | null) => (m?.length ?? 0) > 0 && (
    <div style={{ marginTop: 2 }}>
      {m!.slice(0, 3).map((x) => <Tag key={x} color="gold" style={{ marginBottom: 2 }}>{x}</Tag>)}
      {m!.length > 3 && <Typography.Text type="secondary">+{m!.length - 3}</Typography.Text>}
    </div>
  )
  const storneaza = useStorneazaNir()
  const [facturaId, setFacturaId] = useState<string>()
  const [mod, setMod] = useState<ModReceptie>('tot')
  const deschide = (id: string, m: ModReceptie) => { setMod(m); setFacturaId(id) }
  const [deStornat, setDeStornat] = useState<Nir>()
  const [deVazut, setDeVazut] = useState<FacturaDeReceptionat>()
  const [formStorno] = Form.useForm<{ motiv: string }>()
  const deStornatAnulate = (nir.data ?? []).filter((n) => n.facturaAnulata && !n.storno)
  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="large">
      <Form layout="inline" style={{ rowGap: 8 }}
        onFinish={(v: FiltreReceptii) => setFiltre({ cauta: v.cauta?.trim() || undefined, material: v.material?.trim() || undefined })}>
        <Form.Item name="material">
          <Input allowClear prefix={<SearchOutlined />} placeholder="Material (denumire sau cod)" style={{ width: 260 }} />
        </Form.Item>
        <Form.Item name="cauta">
          <Input allowClear placeholder="Factura sau furnizorul" style={{ width: 220 }} />
        </Form.Item>
        <Button type="primary" htmlType="submit">Filtrează</Button>
        {filtrat && <Typography.Text type="secondary">Filtrul se aplică facturilor de recepționat și NIR-urilor emise.</Typography.Text>}
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
      {deStornatAnulate.length > 0 && (
        <Alert type="warning" showIcon
          title={`Facturi anulate cu NIR activ: ${deStornatAnulate.map((n) => `${n.numar} (factura ${n.facturaNumar})`).join(', ')}.`}
          description="Materialele lor sunt încă în stoc. Stornează NIR-ul (după returul în depozit, dacă au plecat deja în șantier)." />
      )}
      <Card title={`NIR-uri emise${filtrat && nir.data ? ` (${nir.data.length})` : ''}`}>
        <Table<Nir>
          rowKey="id"
          size="small"
          loading={nir.isLoading}
          dataSource={nir.data}
          locale={filtrat ? { emptyText: 'Niciun NIR nu corespunde filtrului.' } : undefined}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          expandable={{
            expandedRowRender: (n) => (
              <Table size="small" pagination={false} rowKey={(l) => l.articolCod + l.denumire} dataSource={n.linii}
                columns={[
                  { title: 'Cod', dataIndex: 'articolCod', width: 110 },
                  { title: 'Material', dataIndex: 'denumire' },
                  { title: 'Facturat', width: 110, align: 'right', render: (_, l) => formatCantitate(l.cantitateFactura) },
                  {
                    title: 'Recepționat', width: 130, align: 'right',
                    render: (_, l) => <Typography.Text type={l.cantitateReceptionata !== l.cantitateFactura ? 'danger' : undefined}>{formatCantitate(l.cantitateReceptionata)} {l.unitateMasura}</Typography.Text>,
                  },
                  { title: 'Preț', dataIndex: 'pretUnitar', width: 110, align: 'right', render: bani },
                  { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right', render: bani },
                  { title: 'Observații', dataIndex: 'observatii' },
                ]} />
            ),
          }}
          columns={[
            { title: 'Număr', dataIndex: 'numar', width: 140 },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Factura', dataIndex: 'facturaNumar', width: 140 },
            { title: 'Furnizor', render: (_, n) => <>{n.furnizorDenumire}{gasite(n.materialeGasite)}</> },
            { title: 'Depozit', dataIndex: 'gestiune', width: 180 },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
            {
              title: '', width: 200,
              render: (_, n) => (
                <Space size={4} wrap>
                  {n.areDiferente && <Tag color="red">cu diferențe</Tag>}
                  {n.storno && <Tag color="volcano" title={`${dayjs(n.storno.data).format('DD.MM.YYYY')}: ${n.storno.motiv} (${n.storno.stornatDe})`}>stornat</Tag>}
                  {n.facturaAnulata && !n.storno && <Tag color="orange">factură anulată</Tag>}
                </Space>
              ),
            },
            { title: 'Recepționat de', dataIndex: 'receptionatDe', width: 160 },
            {
              title: '', width: 170,
              render: (_, n) => (
                <Space size={4}>
                  <Button size="small" icon={<FilePdfOutlined />} onClick={(e) => { e.stopPropagation(); void deschidePdf(`/api/stocuri/nir/${n.id}/pdf`).catch((x) => message.error((x as Error).message)) }}>PDF</Button>
                  {!n.storno && poateAnula && <Button size="small" danger onClick={(e) => { e.stopPropagation(); setDeStornat(n) }}>Stornează</Button>}
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
      <Modal open={!!deStornat} title={deStornat && `Stornează ${deStornat.numar} (factura ${deStornat.facturaNumar})`} okText="Stornează" okButtonProps={{ danger: true }}
        cancelText="Renunță" onCancel={() => setDeStornat(undefined)} onOk={() => formStorno.submit()} confirmLoading={storneaza.isPending} destroyOnHidden>
        <Alert type="warning" showIcon style={{ marginBottom: 12 }}
          title="Materialele recepționate ies din depozit la valoarea cu care au intrat. Dacă factura nu e anulată, va putea fi recepționată din nou." />
        <Form form={formStorno} layout="vertical" name="storno" onFinish={async (v) => {
          try {
            await storneaza.mutateAsync({ id: deStornat!.id, data: dayjs().format('YYYY-MM-DD'), motiv: v.motiv.trim() })
            message.success(`${deStornat!.numar} a fost stornat.`)
            setDeStornat(undefined)
          } catch (e) {
            message.error((e as Error).message)
          }
        }}>
          <Form.Item name="motiv" label="Motivul stornării" rules={[{ required: true, whitespace: true, message: 'Motivul este obligatoriu.' }]}>
            <Input.TextArea rows={3} maxLength={1000} placeholder="ex. factura a fost anulată de furnizor / recepție înregistrată greșit" />
          </Form.Item>
        </Form>
      </Modal>
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
