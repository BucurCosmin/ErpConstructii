import { DeleteOutlined, PaperClipOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Flex, Input, Popconfirm, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useAvize, useStergeAviz } from '../../api/avize'
import type { Aviz } from '../../api/avize'
import { api } from '../../api/client'
import { useProiecte } from '../../api/interogari'
import { TagDinUrma } from '../../componente/DocumentDinUrma'
import { ButonRaport } from '../../componente/NumereHartie'
import { etichetaProiect } from '../../util/format'

const cantitate = (v: number) => v.toLocaleString('ro-RO', { maximumFractionDigits: 4 })

type StareMarfa = 'deReceptionat' | 'faraFactura' | 'facturat'

const stareMarfa = (a: Aviz): StareMarfa => (a.receptii === 0 ? 'deReceptionat' : a.facturat ? 'facturat' : 'faraFactura')

const etichetaStare: Record<StareMarfa, string> = {
  deReceptionat: 'de recepționat',
  faraFactura: 'recepționat, fără factură',
  facturat: 'facturat',
}

/** Rapoartele avizelor (D-40), pe cele afișate (cu filtrele active). */
const TIPURI_RAPORT_AVIZE = [
  { key: 'sintetic', label: 'Sintetic pe furnizori și lucrări (avize, poziții, stare marfă)' },
  { key: 'materiale', label: 'Materiale primite pe lucrare (cantități adunate)' },
  { key: 'registru', label: 'Registru (un rând pe aviz)' },
  { key: 'detaliat', label: 'Detaliat (avizele cu materialele lor)' },
]

function exportCsv(avize: Aviz[]) {
  const camp = (v: string | number | null) => {
    const t = v === null ? '' : typeof v === 'number' ? String(v).replace('.', ',') : v
    return /[;"\r\n]/.test(t) ? `"${t.replaceAll('"', '""')}"` : t
  }
  const randuri = [
    ['Nr. aviz', 'Înregistrat', 'Data', 'Proiect', 'Furnizor', 'CUI', 'Notă de comandă', 'Material', 'UM', 'Cantitate', 'Marfa'],
    ...avize.flatMap((a) => a.linii.map((l) => [a.numarExtern, a.numar, dayjs(a.data).format('DD.MM.YYYY'), a.proiectCod, a.furnizorDenumire,
      a.furnizorCui, a.notaComandaNumar, l.denumire, l.unitateMasura, l.cantitate, etichetaStare[stareMarfa(a)]])),
  ]
  const url = URL.createObjectURL(new Blob(['\ufeff' + randuri.map((x) => x.map(camp).join(';')).join('\r\n')], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `avize-materiale-${dayjs().format('YYYY-MM-DD')}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

/**
 * Avizele de însoțire înregistrate (D-40), cu starea mărfii: de recepționat (Stocuri → Recepții), recepționată pe aviz și
 * încă fără factură (stoc cu valoare provizorie) sau facturată (factura legată de aceeași notă de comandă).
 */
export function ListaAvize({ poateCrea, onImport }: { poateCrea: boolean; onImport: () => void }) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const proiecte = useProiecte()
  const [proiectId, setProiectId] = useState<string>()
  const [cauta, setCauta] = useState<string>()
  const avize = useAvize({ proiectId, cauta })
  const sterge = useStergeAviz()
  const [perioada, setPerioada] = useState<[Dayjs | null, Dayjs | null] | null>(null)
  const [stare, setStare] = useState<StareMarfa>()
  const [material, setMaterial] = useState<string>()

  // Filtrele din ecran (perioadă, stare, material) se aplică pe lista primită; rapoartele iau exact avizele afișate.
  const afisate = useMemo(() => {
    const [de, panaLa] = perioada ?? []
    const m = material?.trim().toLowerCase()
    return (avize.data ?? []).filter((a) =>
      (!de || !dayjs(a.data).isBefore(de, 'day')) &&
      (!panaLa || !dayjs(a.data).isAfter(panaLa, 'day')) &&
      (!stare || stareMarfa(a) === stare) &&
      (!m || a.linii.some((l) => l.denumire.toLowerCase().includes(m))))
  }, [avize.data, perioada, stare, material])

  const descriereFiltre = () => {
    const p: string[] = []
    const [de, panaLa] = perioada ?? []
    if (de || panaLa) p.push(`perioada ${de ? de.format('DD.MM.YYYY') : '…'} – ${panaLa ? panaLa.format('DD.MM.YYYY') : '…'}`)
    const pr = proiecte.data?.find((x) => x.id === proiectId)
    if (pr) p.push(`proiectul ${pr.cod}`)
    if (cauta) p.push(`„${cauta}”`)
    if (stare) p.push(etichetaStare[stare])
    if (material?.trim()) p.push(`materialul „${material.trim()}”`)
    return p.join(', ')
  }

  const deschideOriginal = async (a: Aviz) => {
    try {
      const blob = await api.getBinar(`/api/aprovizionare/proiecte/${a.proiectId}/avize/${a.id}/original`)
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const stergeAviz = async (a: Aviz) => {
    try {
      await sterge.mutateAsync({ proiectId: a.proiectId, id: a.id })
      message.success(`Avizul ${a.numarExtern} a fost șters${a.notaComandaGenerata ? ' (cu nota de comandă generată din el, dacă nu era folosită)' : ''}.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Card
      title="Avize de însoțire a mărfii"
      extra={
        <Space wrap>
          <ButonRaport url="/api/aprovizionare/avize/raport/pdf" ids={afisate.map((a) => a.id)} filtre={descriereFiltre()}
            tipuri={TIPURI_RAPORT_AVIZE} csv={() => exportCsv(afisate)} />
          <Button type="primary" icon={<PlusOutlined />} disabled={!poateCrea} onClick={onImport}>Importă aviz</Button>
        </Space>
      }
    >
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Select
          allowClear
          showSearch={{ optionFilterProp: 'label' }}
          placeholder="Toate proiectele"
          style={{ width: 280 }}
          value={proiectId}
          onChange={setProiectId}
          options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))}
        />
        <Input.Search allowClear placeholder="Număr aviz, furnizor sau notă de comandă" style={{ width: 320 }} onSearch={(v) => setCauta(v.trim() || undefined)} />
        <DatePicker.RangePicker format="DD.MM.YYYY" allowEmpty={[true, true]} placeholder={['De la', 'Până la']} value={perioada} onChange={(v) => setPerioada(v)} />
        <Select allowClear placeholder="Marfa: toate" style={{ width: 220 }} value={stare} onChange={setStare}
          options={(Object.keys(etichetaStare) as StareMarfa[]).map((k) => ({ value: k, label: etichetaStare[k] }))} />
        <Input allowClear placeholder="Material" style={{ width: 200 }} value={material} onChange={(e) => setMaterial(e.target.value || undefined)} />
      </Flex>
      {avize.error && <Alert type="error" showIcon title="Avizele nu au putut fi încărcate." description={avize.error.message} />}
      <Table<Aviz>
        rowKey="id"
        size="small"
        loading={avize.isLoading}
        dataSource={afisate}
        pagination={{ pageSize: 20, hideOnSinglePage: true }}
        expandable={{
          expandedRowRender: (a) => (
            <Table
              size="small"
              rowKey={(_, i) => String(i)}
              pagination={false}
              dataSource={a.linii}
              columns={[
                { title: 'Denumire', dataIndex: 'denumire' },
                { title: 'UM', dataIndex: 'unitateMasura', width: 80 },
                { title: 'Cantitate', dataIndex: 'cantitate', width: 120, align: 'right', render: cantitate },
              ]}
            />
          ),
        }}
        columns={[
          {
            title: 'Aviz',
            key: 'numar',
            render: (_, a) => (
              <>
                <Typography.Text strong>{a.numarExtern}</Typography.Text>
                <br />
                <Typography.Text type="secondary">{a.numar}</Typography.Text>
                {a.dinUrma && <> <TagDinUrma /></>}
              </>
            ),
          },
          { title: 'Data', dataIndex: 'data', width: 105, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
          { title: 'Furnizor', dataIndex: 'furnizorDenumire', ellipsis: true },
          { title: 'Proiect', dataIndex: 'proiectCod', width: 110 },
          {
            title: 'Notă de comandă',
            key: 'nota',
            width: 170,
            render: (_, a) => (
              <>
                {a.notaComandaNumar ?? <Typography.Text type="secondary">— (din urmă)</Typography.Text>}
                {a.notaComandaGenerata && (
                  <Tooltip title="Generată din aviz: marfa a venit fără comandă în sistem">
                    <Tag style={{ marginInlineStart: 6 }}>din aviz</Tag>
                  </Tooltip>
                )}
              </>
            ),
          },
          { title: 'Materiale', key: 'linii', width: 90, align: 'right', render: (_, a) => a.linii.length },
          {
            title: 'Stoc',
            key: 'stare',
            width: 200,
            render: (_, a) =>
              a.receptii === 0 ? (
                <Tooltip title="Deschide recepțiile">
                  <Tag color="gold" style={{ cursor: 'pointer' }} onClick={() => navigheaza('/stocuri?tab=receptii')}>de recepționat</Tag>
                </Tooltip>
              ) : a.facturat ? (
                <Tag color="green">facturat: {a.facturaNumar}</Tag>
              ) : (
                <Tooltip title="Recepționat pe aviz, fără valoare definitivă: se completează când factura (legată de aceeași notă de comandă) e legată de NIR.">
                  <Tag color="orange">recepționat, fără factură</Tag>
                </Tooltip>
              ),
          },
          {
            title: '',
            key: 'actiuni',
            width: 80,
            render: (_, a) => (
              <Flex gap={4}>
                <Tooltip title="Documentul original">
                  <Button size="small" type="text" icon={<PaperClipOutlined />} onClick={() => void deschideOriginal(a)} />
                </Tooltip>
                <Popconfirm
                  title={`Ștergi avizul ${a.numarExtern}?`}
                  description="Doar cât nu are recepții. Nota de comandă generată din el se șterge și ea, dacă nu o mai folosește nimic."
                  okText="Șterge"
                  cancelText="Nu"
                  onConfirm={() => stergeAviz(a)}
                  disabled={!poateCrea || a.receptii > 0}
                >
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} disabled={!poateCrea || a.receptii > 0} />
                </Popconfirm>
              </Flex>
            ),
          },
        ]}
      />
    </Card>
  )
}
