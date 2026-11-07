import { DownloadOutlined, EditOutlined, FilePdfOutlined, PrinterOutlined, SearchOutlined, StopOutlined, WarningOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Dropdown, Form, Input, Modal, Segmented, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { api } from '../../api/client'
import { useAnuleazaFactura, useFacturiPrimite } from '../../api/facturi'
import type { FacturaPrimita, FiltreFacturi, LinieFactura } from '../../api/facturi'
import { useProiecte } from '../../api/interogari'
import { formatData, formatLei } from '../../util/format'
import { deschidePdf } from '../../util/pdf'
import { CorectieFactura } from './CorectieFactura'

type Grupare = 'Furnizor' | 'Proiect' | 'Luna' | 'CotaTva'

interface ValoriFiltre {
  proiectId?: string
  cauta?: string
  material?: string
  perioada?: [Dayjs, Dayjs]
  stare?: FacturaPrimita['stare']
  sursa?: FacturaPrimita['sursa']
  moneda?: string
}

interface TotalGrup {
  cheie: string
  eticheta: string
  moneda: string
  facturi: number
  baza: number
  tva: number
  total: number
}

const rotunjeste = (v: number) => Math.round(v * 100) / 100

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nume
  a.click()
  URL.revokeObjectURL(url)
}

/** CSV pentru Excel (separator „;”, BOM pentru diacritice). */
function csv(cap: string[], randuri: (string | number | null)[][], nume: string) {
  const camp = (v: string | number | null) => {
    if (v === null) return ''
    const s = typeof v === 'number' ? v.toString().replace('.', ',') : v
    return /[;"\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  descarca(new Blob(['﻿' + [cap.join(';'), ...randuri.map((r) => r.map(camp).join(';'))].join('\r\n')], { type: 'text/csv;charset=utf-8' }), nume)
}

const stare = (f: FacturaPrimita) => (f.stare === 'Anulata' ? 'anulată' : f.esteStorno ? 'storno' : 'înregistrată')

/** Totalurile pe ecran (aceeași regulă ca raportul PDF: fără anulate, pe monedă; cota TVA din linii). */
function totaluri(facturi: FacturaPrimita[], grupare: Grupare): TotalGrup[] {
  const m = new Map<string, TotalGrup & { id: Set<string> }>()
  const adauga = (cheie: string, eticheta: string, moneda: string, id: string, baza: number, tva: number, total: number) => {
    const k = `${moneda}|${cheie}`
    const g = m.get(k) ?? { cheie, eticheta, moneda, facturi: 0, baza: 0, tva: 0, total: 0, id: new Set<string>() }
    g.id.add(id)
    g.baza += baza
    g.tva += tva
    g.total += total
    m.set(k, g)
  }
  for (const f of facturi.filter((x) => x.stare !== 'Anulata')) {
    if (grupare === 'CotaTva') {
      // Liniile greșite (nu dau totalul): factura intră cu totalurile ei, pe cota rezultată din ele (ca raportul PDF).
      const parti = f.liniiConcordante
        ? f.linii.map((l) => ({ cota: l.cotaTva, baza: l.valoareFaraTva, tva: l.valoareTva }))
        : [{ cota: f.totalFaraTva ? Math.round((f.totalTva / f.totalFaraTva) * 100) / 100 : 0, baza: f.totalFaraTva, tva: f.totalTva }]
      for (const l of parti) {
        const p = `${Math.round(l.cota * 10000) / 100}`
        adauga(p.padStart(6, '0'), `TVA ${p}%`, f.moneda, f.id, l.baza, l.tva, l.baza + l.tva)
      }
    } else if (grupare === 'Furnizor') {
      adauga(f.furnizorCui || f.furnizorDenumire.toUpperCase(), f.furnizorCui ? `${f.furnizorDenumire} (${f.furnizorCui})` : f.furnizorDenumire, f.moneda, f.id, f.totalFaraTva, f.totalTva, f.totalDePlata)
    } else if (grupare === 'Proiect') {
      adauga(f.proiectId, f.proiectCod, f.moneda, f.id, f.totalFaraTva, f.totalTva, f.totalDePlata)
    } else {
      const luna = dayjs(f.dataEmitere)
      adauga(luna.format('YYYY-MM'), luna.format('MMMM YYYY'), f.moneda, f.id, f.totalFaraTva, f.totalTva, f.totalDePlata)
    }
  }
  return [...m.values()]
    .map(({ id, ...g }) => ({ ...g, facturi: id.size, baza: rotunjeste(g.baza), tva: rotunjeste(g.tva), total: rotunjeste(g.total) }))
    .sort((a, b) => a.moneda.localeCompare(b.moneda) || (grupare === 'Luna' ? a.cheie.localeCompare(b.cheie) : grupare === 'CotaTva' ? b.cheie.localeCompare(a.cheie) : b.total - a.total))
}

/**
 * Facturile primite: filtre (proiect, furnizor / CUI / număr, material, perioadă, stare, sursă, monedă), tabel compact
 * cu totaluri pe monedă, totaluri pe furnizor / proiect / lună / cotă TVA, rapoarte PDF, tipărirea originalelor și export CSV.
 */
export function ListaFacturiPrimite({ poateCrea, poateAnula, poateEdita, onImport }: { poateCrea: boolean; poateAnula: boolean; poateEdita: boolean; onImport: () => void }) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const [filtre, setFiltre] = useState<FiltreFacturi>({ stare: 'Inregistrata' })
  const [filtreLocale, setFiltreLocale] = useState<{ sursa?: FacturaPrimita['sursa']; moneda?: string }>({})
  const facturi = useFacturiPrimite(filtre)
  const anuleaza = useAnuleazaFactura()
  const [deAnulat, setDeAnulat] = useState<FacturaPrimita>()
  const [deCorectat, setDeCorectat] = useState<FacturaPrimita>()
  const [formAnulare] = Form.useForm<{ motiv: string }>()
  const [selectate, setSelectate] = useState<string[]>([])
  const [grupare, setGrupare] = useState<Grupare>('Furnizor')
  const [seGenereaza, setSeGenereaza] = useState(false)

  const lista = useMemo(
    () => (facturi.data ?? []).filter((f) => (!filtreLocale.sursa || f.sursa === filtreLocale.sursa) && (!filtreLocale.moneda || f.moneda === filtreLocale.moneda)),
    [facturi.data, filtreLocale],
  )
  const monede = useMemo(() => [...new Set((facturi.data ?? []).map((f) => f.moneda))].sort(), [facturi.data])
  const alese = selectate.length ? lista.filter((f) => selectate.includes(f.id)) : lista
  const totalPeMoneda = useMemo(() => {
    const m = new Map<string, { baza: number; tva: number; total: number; nr: number }>()
    for (const f of lista.filter((x) => x.stare !== 'Anulata')) {
      const t = m.get(f.moneda) ?? { baza: 0, tva: 0, total: 0, nr: 0 }
      t.baza += f.totalFaraTva
      t.tva += f.totalTva
      t.total += f.totalDePlata
      t.nr++
      m.set(f.moneda, t)
    }
    return [...m.entries()].sort(([a], [b]) => (a === 'RON' ? -1 : b === 'RON' ? 1 : a.localeCompare(b)))
  }, [lista])
  const grupuri = useMemo(() => totaluri(alese, grupare), [alese, grupare])

  const descriereFiltre = () => {
    const p: string[] = []
    if (filtre.dataDe || filtre.dataPanaLa) p.push(`perioada ${filtre.dataDe ? formatData(filtre.dataDe) : '…'} – ${filtre.dataPanaLa ? formatData(filtre.dataPanaLa) : '…'}`)
    const pr = proiecte.data?.find((x) => x.id === filtre.proiectId)
    if (pr) p.push(`proiectul ${pr.cod}`)
    if (filtre.cauta) p.push(`„${filtre.cauta}”`)
    if (filtre.material) p.push(`materialul „${filtre.material}”`)
    if (filtre.stare) p.push(filtre.stare === 'Anulata' ? 'doar anulate' : 'doar înregistrate')
    if (filtreLocale.sursa) p.push(filtreLocale.sursa === 'EFactura' ? 'din e-Factura' : 'din PDF')
    if (filtreLocale.moneda) p.push(`moneda ${filtreLocale.moneda}`)
    if (selectate.length) p.push(`${selectate.length} facturi bifate`)
    return p.join(', ')
  }

  const pdf = async (url: string, corp: unknown) => {
    setSeGenereaza(true)
    try {
      await deschidePdf(url, corp)
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeGenereaza(false)
    }
  }
  const raport = (tip: string) => void pdf('/api/facturi/primite/raport/pdf', { id: alese.map((f) => f.id), tip, filtre: descriereFiltre() })

  const original = async (f: FacturaPrimita, tip: 'xml' | 'pdf') => {
    try {
      descarca(await api.getBinar(`/api/facturi/primite/${f.id}/original/${tip}`), `${f.numar.replace(/[^a-zA-Z0-9_-]/g, '_')}.${tip}`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const exportFacturi = () => csv(
    ['Număr', 'Data', 'Scadența', 'Furnizor', 'CUI', 'Proiect', 'Bază', 'TVA', 'Total', 'Monedă', 'Sursă', 'Stare', 'Referință', 'Înregistrată de'],
    alese.map((f) => [f.numar, formatData(f.dataEmitere), f.dataScadenta ? formatData(f.dataScadenta) : '', f.furnizorDenumire, f.furnizorCui, f.proiectCod,
      f.totalFaraTva, f.totalTva, f.totalDePlata, f.moneda, f.sursa === 'EFactura' ? 'e-Factura' : 'PDF', stare(f), f.referinta, f.inregistrataDeNume]),
    `facturi-primite-${dayjs().format('YYYY-MM-DD')}.csv`)
  const exportLinii = () => csv(
    ['Număr factură', 'Data', 'Furnizor', 'CUI', 'Proiect', 'Material / serviciu', 'UM', 'Cantitate', 'Preț unitar', 'Valoare', 'Cotă TVA %', 'TVA', 'Intră în stoc', 'Monedă', 'Stare'],
    alese.flatMap((f) => f.linii.map((l) => [f.numar, formatData(f.dataEmitere), f.furnizorDenumire, f.furnizorCui, f.proiectCod, l.denumire, l.unitateMasura,
      l.cantitate, l.pretUnitar, l.valoareFaraTva, Math.round(l.cotaTva * 10000) / 100, l.valoareTva, l.intraInStoc ? 'da' : 'nu', f.moneda, stare(f)])),
    `linii-facturi-primite-${dayjs().format('YYYY-MM-DD')}.csv`)

  const confirmaAnularea = async ({ motiv }: { motiv: string }) => {
    if (!deAnulat) return
    try {
      await anuleaza.mutateAsync({ id: deAnulat.id, motiv: motiv.trim() })
      message.success(`Factura ${deAnulat.numar} a fost anulată.`)
      setDeAnulat(undefined)
      formAnulare.resetFields()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const nr = alese.length
  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Form<ValoriFiltre>
        layout="inline"
        style={{ rowGap: 8 }}
        initialValues={{ stare: 'Inregistrata' }}
        onFinish={(v) => {
          setSelectate([])
          setFiltre({
            proiectId: v.proiectId, cauta: v.cauta?.trim() || undefined, material: v.material?.trim() || undefined, stare: v.stare,
            dataDe: v.perioada?.[0]?.format('YYYY-MM-DD'), dataPanaLa: v.perioada?.[1]?.format('YYYY-MM-DD'),
          })
          setFiltreLocale({ sursa: v.sursa, moneda: v.moneda })
        }}
      >
        <Form.Item name="perioada">
          <DatePicker.RangePicker format="DD.MM.YYYY" allowEmpty={[true, true]}
            presets={[
              { label: 'Luna curentă', value: [dayjs().startOf('month'), dayjs()] },
              { label: 'Luna trecută', value: [dayjs().subtract(1, 'month').startOf('month'), dayjs().subtract(1, 'month').endOf('month')] },
              { label: 'Trimestrul curent', value: [dayjs().startOf('month').subtract(dayjs().month() % 3, 'month'), dayjs()] },
              { label: 'Anul curent', value: [dayjs().startOf('year'), dayjs()] },
              { label: 'Anul trecut', value: [dayjs().subtract(1, 'year').startOf('year'), dayjs().subtract(1, 'year').endOf('year')] },
            ]} />
        </Form.Item>
        <Form.Item name="proiectId">
          <Select allowClear placeholder="Toate proiectele" style={{ width: 220 }} showSearch={{ optionFilterProp: 'label' }}
            options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
        </Form.Item>
        <Form.Item name="cauta">
          <Input allowClear placeholder="Furnizor, CUI sau număr" style={{ width: 200 }} />
        </Form.Item>
        <Form.Item name="material">
          <Input allowClear prefix={<SearchOutlined />} placeholder="Material din factură" style={{ width: 190 }} />
        </Form.Item>
        <Form.Item name="stare">
          <Select allowClear placeholder="Orice stare" style={{ width: 140 }}
            options={[{ value: 'Inregistrata', label: 'Înregistrate' }, { value: 'Anulata', label: 'Anulate' }]} />
        </Form.Item>
        <Form.Item name="sursa">
          <Select allowClear placeholder="Orice sursă" style={{ width: 130 }} options={[{ value: 'EFactura', label: 'e-Factura' }, { value: 'Pdf', label: 'PDF' }]} />
        </Form.Item>
        {monede.length > 1 && (
          <Form.Item name="moneda">
            <Select allowClear placeholder="Orice monedă" style={{ width: 130 }} options={monede.map((m) => ({ value: m, label: m }))} />
          </Form.Item>
        )}
        <Button type="primary" htmlType="submit">Filtrează</Button>
        <Button style={{ marginInlineStart: 'auto' }} onClick={onImport} disabled={!poateCrea}>Importă factură</Button>
      </Form>

      {facturi.error && <Alert type="error" showIcon title={(facturi.error as Error).message} />}

      <Card
        size="small"
        title={
          <Space wrap size="large">
            <span>{lista.length} facturi</span>
            {totalPeMoneda.map(([m, t]) => (
              <Typography.Text key={m} type="secondary" style={{ fontWeight: 'normal' }}>
                {m}: bază <b>{formatLei(t.baza)}</b> · TVA <b>{formatLei(t.tva)}</b> · total <b>{formatLei(t.total)}</b>
              </Typography.Text>
            ))}
          </Space>
        }
        extra={
          <Space wrap>
            {selectate.length > 0 && <Tag color="blue" closable onClose={() => setSelectate([])}>{selectate.length} bifate</Tag>}
            <Dropdown
              disabled={nr === 0}
              menu={{
                items: [
                  { key: 'Registru', label: 'Registrul facturilor' },
                  { type: 'divider' },
                  { key: 'Furnizor', label: 'Totaluri pe furnizori' },
                  { key: 'Proiect', label: 'Totaluri pe proiecte' },
                  { key: 'Luna', label: 'Totaluri pe luni' },
                  { key: 'CotaTva', label: 'Defalcare pe cote TVA' },
                ],
                onClick: ({ key }) => raport(key),
              }}
            >
              <Button icon={<FilePdfOutlined />} loading={seGenereaza}>Rapoarte PDF{selectate.length ? ' (bifate)' : ''}</Button>
            </Dropdown>
            <Tooltip title="Originalele PDF (scanate) ale facturilor, într-un singur fișier de tipărit">
              <Button icon={<PrinterOutlined />} disabled={nr === 0} loading={seGenereaza}
                onClick={() => nr > 100 ? message.warning('Se pot tipări cel mult 100 de facturi deodată: bifează mai puține.') : void pdf('/api/facturi/primite/originale/pdf', { id: alese.map((f) => f.id) })}>
                Tipărește originalele ({nr})
              </Button>
            </Tooltip>
            <Dropdown disabled={nr === 0}
              menu={{ items: [{ key: 'f', label: 'Facturi (o linie pe factură)' }, { key: 'l', label: 'Linii (materiale și servicii)' }], onClick: ({ key }) => (key === 'f' ? exportFacturi() : exportLinii()) }}>
              <Button icon={<DownloadOutlined />}>Excel (CSV)</Button>
            </Dropdown>
          </Space>
        }
      >
        <Table<FacturaPrimita>
          rowKey="id"
          size="small"
          className="tabel-compact"
          tableLayout="fixed"
          scroll={{ x: 1150 }}
          loading={facturi.isLoading}
          dataSource={lista}
          rowSelection={{ selectedRowKeys: selectate, onChange: (k) => setSelectate(k as string[]) }}
          rowClassName={(f) => (f.stare === 'Anulata' ? 'rand-inactiv' : '')}
          pagination={{ defaultPageSize: 50, showSizeChanger: true, pageSizeOptions: [25, 50, 100, 200], hideOnSinglePage: true, size: 'small' }}
          expandable={{
            expandedRowRender: (f) => (
              <div>
                <Table<LinieFactura>
                  rowKey={(l) => `${l.denumire}-${l.cantitate}-${l.valoareFaraTva}`}
                  size="small"
                  className="tabel-compact"
                  tableLayout="fixed"
                  pagination={false}
                  dataSource={f.linii}
                  columns={[
                    { title: 'Denumire', dataIndex: 'denumire', ellipsis: { showTitle: true } },
                    { title: 'UM', dataIndex: 'unitateMasura', width: 60 },
                    { title: 'Cantitate', dataIndex: 'cantitate', width: 90, className: 'numeric' },
                    { title: 'Preț unitar', dataIndex: 'pretUnitar', width: 100, className: 'numeric', render: formatLei },
                    { title: 'Valoare', dataIndex: 'valoareFaraTva', width: 110, className: 'numeric', render: formatLei },
                    { title: 'TVA', width: 130, className: 'numeric', render: (_, l) => `${formatLei(l.valoareTva)} (${Math.round(l.cotaTva * 100)}%)` },
                    { title: 'Stoc', width: 60, render: (_, l) => (l.intraInStoc ? <Tag color="blue">da</Tag> : null) },
                  ]}
                />
                <Space wrap size={16} style={{ marginTop: 6 }}>
                  {f.referinta && <Typography.Text type="secondary">Referință: {f.referinta}</Typography.Text>}
                  <Typography.Text type="secondary">Înregistrată de {f.inregistrataDeNume}, {formatData(f.inregistrataLa)}</Typography.Text>
                  {f.observatii && <Typography.Text type="secondary">Observații: {f.observatii}</Typography.Text>}
                </Space>
                {f.motivAnulare && <Alert type="error" showIcon title={`Anulată: ${f.motivAnulare}`} style={{ marginTop: 8 }} />}
              </div>
            ),
          }}
          columns={[
            {
              title: 'Număr', dataIndex: 'numar', width: 165, ellipsis: { showTitle: true }, sorter: (a, b) => a.numar.localeCompare(b.numar),
              render: (n: string, f) => (
                <>
                  {n} {f.esteStorno && <Tag color="volcano">storno</Tag>}
                  {f.corectii > 0 && <Tooltip title={`Corectată de ${f.corectii} ori (istoricul e în fereastra de corectare)`}><EditOutlined style={{ color: '#1677ff', marginInlineStart: 4 }} /></Tooltip>}
                  {!f.liniiConcordante && f.stare !== 'Anulata' && (
                    <Tooltip title="Suma liniilor nu dă totalul facturii: liniile au fost citite greșit la import. Totalurile facturii sunt cele folosite în rapoarte.">
                      <WarningOutlined style={{ color: '#d48806', marginInlineStart: 4 }} />
                    </Tooltip>
                  )}
                </>
              ),
            },
            { title: 'Data', dataIndex: 'dataEmitere', width: 90, render: formatData, sorter: (a, b) => a.dataEmitere.localeCompare(b.dataEmitere), defaultSortOrder: 'descend' },
            {
              title: 'Furnizor', width: 225, sorter: (a, b) => a.furnizorDenumire.localeCompare(b.furnizorDenumire),
              render: (_, f) => (
                <>
                  <div title={f.furnizorCui ? `${f.furnizorDenumire} · CUI ${f.furnizorCui}` : f.furnizorDenumire} style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{f.furnizorDenumire}</div>
                  {(f.materialeGasite?.length ?? 0) > 0 && (
                    <div style={{ marginTop: 2 }}>{f.materialeGasite!.slice(0, 3).map((m) => <Tag key={m} color="gold" style={{ marginBottom: 2 }}>{m}</Tag>)}</div>
                  )}
                </>
              ),
            },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 95, ellipsis: { showTitle: true }, sorter: (a, b) => a.proiectCod.localeCompare(b.proiectCod) },
            { title: 'Scadența', dataIndex: 'dataScadenta', width: 90, render: formatData, responsive: ['xxl'] },
            { title: 'Bază', dataIndex: 'totalFaraTva', width: 105, className: 'numeric', render: formatLei, sorter: (a, b) => a.totalFaraTva - b.totalFaraTva },
            { title: 'TVA', dataIndex: 'totalTva', width: 90, className: 'numeric', render: formatLei, responsive: ['lg'] },
            {
              title: 'Total', width: 120, className: 'numeric', sorter: (a, b) => a.totalDePlata - b.totalDePlata,
              render: (_, f) => <Typography.Text strong>{formatLei(f.totalDePlata)}{f.moneda !== 'RON' && ` ${f.moneda}`}</Typography.Text>,
            },
            {
              title: 'Sursă', width: 85,
              render: (_, f) => f.stare === 'Anulata' ? <Tag color="red">Anulată</Tag> : f.sursa === 'EFactura' ? <Tag color="blue">e-Factura</Tag> : <Tag>PDF</Tag>,
            },
            {
              title: '', width: 140, fixed: 'right',
              render: (_, f) => (
                <Space size={2} onClick={(e) => e.stopPropagation()}>
                  {f.areXml && <Tooltip title="Descarcă XML-ul"><Button size="small" type="text" icon={<DownloadOutlined />} onClick={() => void original(f, 'xml')}>XML</Button></Tooltip>}
                  {f.arePdf && <Tooltip title="Descarcă originalul PDF"><Button size="small" type="text" icon={<DownloadOutlined />} onClick={() => void original(f, 'pdf')}>PDF</Button></Tooltip>}
                  {f.stare === 'Inregistrata' && poateEdita && (
                    <Tooltip title="Corectează datele facturii (ce s-a citit greșit)">
                      <Button size="small" type="text" icon={<EditOutlined />} onClick={() => setDeCorectat(f)} />
                    </Tooltip>
                  )}
                  {f.stare === 'Inregistrata' && poateAnula && (
                    <Tooltip title="Anulează înregistrarea (ex. proiect greșit)">
                      <Button size="small" type="text" danger icon={<StopOutlined />} onClick={() => setDeAnulat(f)} />
                    </Tooltip>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Card
        size="small"
        title={`Totaluri${selectate.length ? ' (facturile bifate)' : ''}`}
        extra={
          <Segmented<Grupare> value={grupare} onChange={setGrupare}
            options={[{ value: 'Furnizor', label: 'Pe furnizori' }, { value: 'Proiect', label: 'Pe proiecte' }, { value: 'Luna', label: 'Pe luni' }, { value: 'CotaTva', label: 'Pe cote TVA' }]} />
        }
      >
        <Table<TotalGrup>
          rowKey={(g) => `${g.moneda}-${g.cheie}`}
          size="small"
          className="tabel-compact"
          tableLayout="fixed"
          dataSource={grupuri}
          pagination={{ defaultPageSize: 20, hideOnSinglePage: true, size: 'small' }}
          columns={[
            { title: { Furnizor: 'Furnizor', Proiect: 'Proiect', Luna: 'Luna', CotaTva: 'Cota TVA' }[grupare], dataIndex: 'eticheta', ellipsis: { showTitle: true } },
            { title: 'Facturi', dataIndex: 'facturi', width: 80, className: 'numeric' },
            { title: 'Bază', dataIndex: 'baza', width: 130, className: 'numeric', render: formatLei },
            { title: 'TVA', dataIndex: 'tva', width: 120, className: 'numeric', render: formatLei },
            { title: 'Total', dataIndex: 'total', width: 140, className: 'numeric', render: (v: number, g) => <b>{formatLei(v)}{g.moneda !== 'RON' && ` ${g.moneda}`}</b> },
            {
              title: '% din total', width: 100, className: 'numeric',
              render: (_, g) => {
                const t = grupuri.filter((x) => x.moneda === g.moneda).reduce((s, x) => s + x.total, 0)
                return t ? `${((g.total / t) * 100).toFixed(1).replace('.', ',')}%` : ''
              },
            },
          ]}
        />
      </Card>

      <CorectieFactura factura={deCorectat} onInchide={() => setDeCorectat(undefined)} />

      <Modal
        title={`Anulează factura ${deAnulat?.numar ?? ''}`}
        open={!!deAnulat}
        onCancel={() => setDeAnulat(undefined)}
        onOk={() => formAnulare.submit()}
        okText="Anulează factura"
        okButtonProps={{ danger: true }}
        cancelText="Renunță"
        confirmLoading={anuleaza.isPending}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">
          Factura rămâne în istoric, marcată ca anulată. Dacă a fost înregistrată pe proiectul greșit, o poți importa din nou pe cel corect.
        </Typography.Paragraph>
        <Form form={formAnulare} layout="vertical" onFinish={confirmaAnularea}>
          <Form.Item name="motiv" label="Motivul anulării" rules={[{ required: true, whitespace: true, message: 'Motivul este obligatoriu.' }]}>
            <Input.TextArea rows={3} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
