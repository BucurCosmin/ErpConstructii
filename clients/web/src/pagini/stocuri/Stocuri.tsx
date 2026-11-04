import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Drawer, Form, Input, Modal, Row, Select, Space, Statistic, Switch, Table, Tabs, Tag, Typography } from 'antd'
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
  useSoldStoc,
} from '../../api/stocuri'
import type { ArticolStoc, FacturaDeReceptionat, MiscareStoc, Nir, SoldStoc } from '../../api/stocuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatCantitate, formatLei } from '../../util/format'
import { ReceptieFactura } from './ReceptieFactura'
import { TransferSantier } from './TransferSantier'

const bani = (v: number | null) => (v === null ? '—' : formatLei(v))
const tipMiscare: Record<MiscareStoc['tip'], { text: string; culoare: string }> = {
  IntrareNir: { text: 'Intrare (NIR)', culoare: 'green' },
  IesireTransfer: { text: 'Ieșire (transfer)', culoare: 'orange' },
  IntrareTransfer: { text: 'Intrare (transfer)', culoare: 'blue' },
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

function Receptii({ poateCrea }: { poateCrea: boolean }) {
  const deFacut = useFacturiDeReceptionat()
  const nir = useNir()
  const [facturaId, setFacturaId] = useState<string>()
  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="large">
      <Card title={`Facturi de recepționat${deFacut.data ? ` (${deFacut.data.length})` : ''}`}>
        <Table<FacturaDeReceptionat>
          rowKey="facturaId"
          size="small"
          loading={deFacut.isLoading}
          dataSource={deFacut.data}
          locale={{ emptyText: 'Toate facturile cu materiale au fost recepționate.' }}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          columns={[
            { title: 'Factura', dataIndex: 'numar', width: 150 },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Furnizor', dataIndex: 'furnizorDenumire' },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 130 },
            { title: 'Linii de stoc', dataIndex: 'linii', width: 110, align: 'right' },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
            {
              title: '',
              width: 140,
              render: (_, f) => <Button type="primary" size="small" disabled={!poateCrea} onClick={() => setFacturaId(f.facturaId)}>Recepționează</Button>,
            },
          ]}
        />
      </Card>
      <Card title="NIR-uri emise">
        <Table<Nir>
          rowKey="id"
          size="small"
          loading={nir.isLoading}
          dataSource={nir.data}
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
            { title: 'Furnizor', dataIndex: 'furnizorDenumire' },
            { title: 'Depozit', dataIndex: 'gestiune', width: 180 },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: bani },
            { title: '', width: 120, render: (_, n) => n.areDiferente && <Tag color="red">cu diferențe</Tag> },
            { title: 'Recepționat de', dataIndex: 'receptionatDe', width: 160 },
          ]}
        />
      </Card>
      <ReceptieFactura facturaId={facturaId} onInchide={() => setFacturaId(undefined)} />
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
          { key: 'receptii', label: 'Recepții (NIR)', children: <Receptii poateCrea={poateCrea} /> },
          { key: 'transfer', label: 'Transfer în șantier', children: <TransferSantier poateCrea={poateCrea} /> },
          { key: 'nomenclator', label: 'Nomenclator materiale', children: <Nomenclator poateEdita={poateEdita} /> },
        ]}
      />
    </>
  )
}
