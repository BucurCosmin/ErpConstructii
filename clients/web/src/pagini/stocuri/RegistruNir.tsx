import { DownloadOutlined, EditOutlined, FilePdfOutlined, HistoryOutlined, PrinterOutlined, RollbackOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Form, Input, Modal, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useProiecte } from '../../api/interogari'
import { useGestiuni, useNir, useStorneazaNir } from '../../api/stocuri'
import type { FiltreNir, Nir } from '../../api/stocuri'
import { LinkFactura } from '../../componente/LinkFactura'
import { formatCantitate, formatLei } from '../../util/format'
import { deschidePdf } from '../../util/pdf'
import { CorectieNir } from './CorectieNir'

const bani = (v: number | null) => (v === null ? '—' : formatLei(v))
const data = (d: string) => dayjs(d).format('DD.MM.YYYY')
const MAXIM_TIPARIRE = 200

interface ValoriFiltre {
  perioada?: [Dayjs, Dayjs]
  gestiuneId?: string
  proiectId?: string
  cauta?: string
  material?: string
  stare?: FiltreNir['stare']
}

/** Raportul pentru Excel: o linie pe material recepționat, cu datele NIR-ului (separator „;”, BOM pentru diacritice). */
function exportaCsv(lista: Nir[]) {
  const cap = ['NIR', 'Data NIR', 'Stare', 'Factura', 'Furnizor', 'Depozit', 'Proiect', 'Cod material', 'Material', 'UM', 'Cantitate factură',
    'Cantitate recepționată', 'Preț unitar', 'Valoare', 'Observații', 'Recepționat de']
  const camp = (v: string | number | null) => {
    if (v === null) return ''
    const s = typeof v === 'number' ? v.toString().replace('.', ',') : v
    return /[;"\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  const randuri = lista.flatMap((n) => n.linii.map((l) => [n.numar, data(n.data), n.storno ? 'stornat' : n.areDiferente ? 'cu diferențe' : 'activ',
    n.facturaNumar, n.furnizorDenumire, n.gestiune, n.proiectCod, l.articolCod, l.denumire, l.unitateMasura, l.cantitateFactura,
    l.cantitateReceptionata, l.pretUnitar, l.valoare, l.observatii, n.receptionatDe].map(camp).join(';')))
  const blob = new Blob(['﻿' + [cap.join(';'), ...randuri].join('\r\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `registru-nir-${dayjs().format('YYYY-MM-DD')}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

/**
 * Registrul NIR-urilor: toate recepțiile, cu filtre (perioadă, depozit, proiect, furnizor / factură / număr, material, stare),
 * tipărirea NIR-urilor (unul, cele bifate sau toate cele filtrate, într-un singur PDF), registrul în PDF și exportul pentru Excel.
 */
export function RegistruNir({ poateAnula, poateEdita }: { poateAnula: boolean; poateEdita: boolean }) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  // Link din corectura unei facturi: NIR-urile ei, indiferent de perioadă.
  const [parametri, setParametri] = useSearchParams()
  const facturaUrl = parametri.get('factura') ?? undefined
  const [deCorectat, setDeCorectat] = useState<Nir>()
  const gestiuni = useGestiuni()
  const proiecte = useProiecte()
  const [filtre, setFiltre] = useState<FiltreNir>(facturaUrl ? { facturaId: facturaUrl } : { dataDe: dayjs().startOf('month').format('YYYY-MM-DD'), dataPanaLa: dayjs().format('YYYY-MM-DD') })
  const nir = useNir(filtre)
  const [selectate, setSelectate] = useState<string[]>([])
  const [deStornat, setDeStornat] = useState<Nir>()
  const [formStorno] = Form.useForm<{ motiv: string }>()
  const storneaza = useStorneazaNir()
  const [seTipareste, setSeTipareste] = useState(false)

  const lista = useMemo(() => nir.data ?? [], [nir.data])
  const active = lista.filter((n) => !n.storno)
  const cuPreturi = lista.some((n) => n.valoare !== null)
  const totalActive = active.reduce((s, n) => s + (n.valoare ?? 0), 0)
  const deStornatAnulate = lista.filter((n) => n.facturaAnulata && !n.storno)

  /** Filtrele în cuvinte, tipărite pe registru. */
  const descriereFiltre = () => {
    const parti: string[] = []
    if (filtre.dataDe || filtre.dataPanaLa) parti.push(`perioada ${filtre.dataDe ? data(filtre.dataDe) : '…'} – ${filtre.dataPanaLa ? data(filtre.dataPanaLa) : '…'}`)
    const g = gestiuni.data?.find((x) => x.id === filtre.gestiuneId)
    if (g) parti.push(`gestiunea ${g.denumire}`)
    const p = proiecte.data?.find((x) => x.id === filtre.proiectId)
    if (p) parti.push(`proiectul ${p.cod}`)
    if (filtre.cauta) parti.push(`„${filtre.cauta}”`)
    if (filtre.material) parti.push(`materialul „${filtre.material}”`)
    if (filtre.stare) parti.push({ Activ: 'doar active', Stornat: 'doar stornate', CuDiferente: 'cu diferențe', Corectat: 'corectate' }[filtre.stare])
    if (filtre.facturaId && lista[0]) parti.push(`factura ${lista[0].facturaNumar}`)
    return parti.join(', ')
  }

  const tipareste = async (url: string, corp: unknown) => {
    setSeTipareste(true)
    try {
      await deschidePdf(url, corp)
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeTipareste(false)
    }
  }

  const tiparesteNir = (ids: string[]) => {
    if (ids.length > MAXIM_TIPARIRE) {
      message.warning(`Se pot tipări cel mult ${MAXIM_TIPARIRE} NIR-uri deodată: restrânge filtrele sau bifează mai puține.`)
      return
    }
    void tipareste('/api/stocuri/nir/pdf', { id: ids })
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Card size="small">
        <Form<ValoriFiltre> layout="inline" style={{ rowGap: 8 }}
          initialValues={{ perioada: [dayjs().startOf('month'), dayjs()] }}
          onFinish={(v) => {
            setSelectate([])
            setFiltre({
              dataDe: v.perioada?.[0]?.format('YYYY-MM-DD'), dataPanaLa: v.perioada?.[1]?.format('YYYY-MM-DD'),
              gestiuneId: v.gestiuneId, proiectId: v.proiectId, stare: v.stare,
              cauta: v.cauta?.trim() || undefined, material: v.material?.trim() || undefined,
            })
          }}>
          <Form.Item name="perioada">
            <DatePicker.RangePicker format="DD.MM.YYYY" allowEmpty={[true, true]}
              presets={[
                { label: 'Luna curentă', value: [dayjs().startOf('month'), dayjs()] },
                { label: 'Luna trecută', value: [dayjs().subtract(1, 'month').startOf('month'), dayjs().subtract(1, 'month').endOf('month')] },
                { label: 'Anul curent', value: [dayjs().startOf('year'), dayjs()] },
              ]} />
          </Form.Item>
          <Form.Item name="gestiuneId">
            <Select allowClear placeholder="Toate depozitele" style={{ width: 200 }}
              options={gestiuni.data?.filter((g) => g.tip === 'Depozit').map((g) => ({ value: g.id, label: g.denumire }))} />
          </Form.Item>
          <Form.Item name="proiectId">
            <Select allowClear placeholder="Toate proiectele" style={{ width: 220 }} showSearch={{ optionFilterProp: 'label' }}
              options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
          </Form.Item>
          <Form.Item name="cauta">
            <Input allowClear placeholder="Nr. NIR, factura sau furnizor" style={{ width: 210 }} />
          </Form.Item>
          <Form.Item name="material">
            <Input allowClear prefix={<SearchOutlined />} placeholder="Material" style={{ width: 170 }} />
          </Form.Item>
          <Form.Item name="stare">
            <Select allowClear placeholder="Orice stare" style={{ width: 150 }}
              options={[{ value: 'Activ', label: 'Active' }, { value: 'Stornat', label: 'Stornate' }, { value: 'CuDiferente', label: 'Cu diferențe' }, { value: 'Corectat', label: 'Corectate' }]} />
          </Form.Item>
          <Button type="primary" htmlType="submit">Filtrează</Button>
        </Form>
      </Card>

      {filtre.facturaId && (
        <Alert type="info" showIcon
          title={lista[0] ? `NIR-urile facturii ${lista[0].facturaNumar} — ${lista[0].furnizorDenumire}` : 'NIR-urile facturii alese'}
          description="Corectează NIR-ul (✎) dacă recepția trebuie adusă la factura corectată."
          action={<Button size="small" onClick={() => { setFiltre({ dataDe: dayjs().startOf('month').format('YYYY-MM-DD'), dataPanaLa: dayjs().format('YYYY-MM-DD') }); setParametri((p) => { p.delete('factura'); return p }, { replace: true }) }}>Toate NIR-urile</Button>} />
      )}

      {deStornatAnulate.length > 0 && (
        <Alert type="warning" showIcon
          title={`Facturi anulate cu NIR activ: ${deStornatAnulate.map((n) => `${n.numar} (factura ${n.facturaNumar})`).join(', ')}.`}
          description="Materialele lor sunt încă în stoc. Stornează NIR-ul (după returul în depozit, dacă au plecat deja în șantier)." />
      )}

      <Card
        title={`NIR-uri: ${lista.length}${lista.length !== active.length ? ` (${active.length} active)` : ''}${cuPreturi ? ` · valoare NIR-uri active ${formatLei(totalActive)} lei` : ''}`}
        extra={
          <Space wrap>
            <Button icon={<PrinterOutlined />} disabled={selectate.length === 0} loading={seTipareste} onClick={() => tiparesteNir(selectate)}>
              Tipărește bifate{selectate.length ? ` (${selectate.length})` : ''}
            </Button>
            <Button icon={<PrinterOutlined />} disabled={lista.length === 0} loading={seTipareste} onClick={() => tiparesteNir(lista.map((n) => n.id))}>
              Tipărește toate ({lista.length})
            </Button>
            <Button icon={<FilePdfOutlined />} disabled={lista.length === 0} loading={seTipareste}
              onClick={() => void tipareste('/api/stocuri/nir/registru/pdf', { id: (selectate.length ? selectate : lista.map((n) => n.id)), filtre: descriereFiltre() })}>
              Registru PDF{selectate.length ? ' (bifate)' : ''}
            </Button>
            <Button icon={<DownloadOutlined />} disabled={lista.length === 0}
              onClick={() => exportaCsv(selectate.length ? lista.filter((n) => selectate.includes(n.id)) : lista)}>
              Excel (CSV)
            </Button>
          </Space>
        }
      >
        <Table<Nir>
          rowKey="id"
          size="small"
          className="tabel-compact"
          tableLayout="fixed"
          scroll={{ x: 1200 }}
          loading={nir.isLoading}
          dataSource={lista}
          locale={{ emptyText: 'Niciun NIR pentru filtrele alese.' }}
          pagination={{ defaultPageSize: 25, showSizeChanger: true, pageSizeOptions: [25, 50, 100, 200], hideOnSinglePage: true }}
          rowSelection={{ selectedRowKeys: selectate, onChange: (k) => setSelectate(k as string[]), preserveSelectedRowKeys: false }}
          expandable={{
            expandedRowRender: (n) => (
              <Table size="small" pagination={false} rowKey={(l) => l.articolCod + l.denumire} dataSource={n.linii}
                columns={[
                  { title: 'Cod', dataIndex: 'articolCod', width: 110 },
                  { title: 'Material', dataIndex: 'denumire' },
                  { title: 'Cf. factură', width: 110, align: 'right', render: (_, l) => formatCantitate(l.cantitateFactura) },
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
            { title: 'Număr', dataIndex: 'numar', width: 120, sorter: (a, b) => a.numar.localeCompare(b.numar) },
            { title: 'Data', dataIndex: 'data', width: 95, render: data, sorter: (a, b) => a.data.localeCompare(b.data) },
            { title: 'Factura', dataIndex: 'facturaNumar', width: 140, ellipsis: { showTitle: true }, render: (_, n) => <LinkFactura id={n.facturaId} numar={n.facturaNumar} /> },
            {
              title: 'Furnizor', width: 230, sorter: (a, b) => a.furnizorDenumire.localeCompare(b.furnizorDenumire),
              render: (_, n) => (
                <>
                  <div title={n.furnizorDenumire} style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{n.furnizorDenumire}</div>
                  {(n.materialeGasite?.length ?? 0) > 0 && (
                    <div style={{ marginTop: 2 }}>{n.materialeGasite!.slice(0, 3).map((x) => <Tag key={x} color="gold" style={{ marginBottom: 2 }}>{x}</Tag>)}</div>
                  )}
                </>
              ),
            },
            { title: 'Depozit', dataIndex: 'gestiune', width: 130, ellipsis: { showTitle: true }, responsive: ['lg'] },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 95, ellipsis: { showTitle: true }, responsive: ['lg'] },
            { title: 'Valoare', dataIndex: 'valoare', width: 110, align: 'right', render: bani, sorter: (a, b) => (a.valoare ?? 0) - (b.valoare ?? 0) },
            {
              title: 'Stare', width: 120,
              render: (_, n) => (
                <Space size={4} wrap>
                  {n.storno
                    ? <Tag color="volcano" title={`${data(n.storno.data)}: ${n.storno.motiv} (${n.storno.stornatDe})`}>stornat</Tag>
                    : <Tag color="green">activ</Tag>}
                  {n.areDiferente && <Tag color="red">diferențe</Tag>}
                  {n.corectii > 0 && <Tag color="blue" title={`${n.corectii} corecturi`}>corectat</Tag>}
                  {n.facturaAnulata && !n.storno && <Tag color="orange">factură anulată</Tag>}
                </Space>
              ),
            },
            { title: 'Recepționat de', dataIndex: 'receptionatDe', width: 130, ellipsis: { showTitle: true }, responsive: ['xxl'] },
            {
              title: '', width: 165, fixed: 'right',
              render: (_, n) => (
                <Space size={4}>
                  <Button size="small" icon={<FilePdfOutlined />}
                    onClick={(e) => { e.stopPropagation(); void deschidePdf(`/api/stocuri/nir/${n.id}/pdf`).catch((x) => message.error((x as Error).message)) }}>PDF</Button>
                  {!n.storno && poateEdita && <Tooltip title="Corectează NIR-ul (cantități, preț); stocul primește diferența"><Button size="small" icon={<EditOutlined />} onClick={(e) => { e.stopPropagation(); setDeCorectat(n) }} /></Tooltip>}
                  <Tooltip title="Istoricul modificărilor"><Button size="small" icon={<HistoryOutlined />} onClick={(e) => { e.stopPropagation(); navigheaza(`/jurnal?document=${encodeURIComponent(n.numar)}`) }} /></Tooltip>
                  {!n.storno && poateAnula && <Tooltip title="Stornează NIR-ul"><Button size="small" danger icon={<RollbackOutlined />} onClick={(e) => { e.stopPropagation(); setDeStornat(n) }} /></Tooltip>}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <CorectieNir nir={deCorectat} onInchide={() => setDeCorectat(undefined)} />

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
