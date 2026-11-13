import { DownloadOutlined } from '@ant-design/icons'
import { Alert, Card, Checkbox, Empty, Flex, Input, Segmented, Select, Space, Switch, Table, Tag, Tooltip, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useAchizitii, useCatalog, useComparatie, useDetaliiAchizitii, usePreturi, useVersiuniCatalog } from '../../api/catalogPreturi'
import type { AchizitieResursa, ObservatiePret, RandComparatie, StatisticaAchizitie, StatisticaPret, VersiuneCatalog } from '../../api/catalogPreturi'
import { useProiecte } from '../../api/interogari'
import { formatCantitate, formatData, formatProcent, stariVersiune, tipuriVersiune } from '../../util/format'
import { descarcaFisier } from '../../util/pdf'

/** Prețurile unitare din devize au 2–4 zecimale. */
const pret = new Intl.NumberFormat('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 4 })
const formatPret = (v: number | null | undefined) => (v === null || v === undefined ? '' : pret.format(v))

const tipuriResursa: Record<string, string> = {
  Material: 'Material',
  Manopera: 'Manoperă',
  Utilaj: 'Utilaj',
  Transport: 'Transport',
  Echipament: 'Echipament',
}

/** Abaterea mare între devize (peste 20%) e semnalată: același cod, prețuri foarte diferite. */
function TagAbatere({ v }: { v: number | null }) {
  if (v === null) return null
  return v === 0 ? <Typography.Text type="secondary">0%</Typography.Text> : <Tag color={v > 0.2 ? 'red' : v > 0.05 ? 'orange' : 'default'}>{formatProcent(v)}</Tag>
}

function csv(randuri: (string | number | null)[][]) {
  const camp = (v: string | number | null) => {
    const s = v === null ? '' : typeof v === 'number' ? String(v).replace('.', ',') : v
    return /[;"\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  return new Blob(['﻿' + randuri.map((r) => r.map(camp).join(';')).join('\r\n')], { type: 'text/csv;charset=utf-8' })
}

/** Numele versiunii și al proiectului, pentru tabele și alegeri. */
function useEtichete() {
  const proiecte = useProiecte()
  const versiuni = useVersiuniCatalog()
  return useMemo(() => {
    const p = new Map((proiecte.data ?? []).map((x) => [x.id, x]))
    const v = new Map((versiuni.data ?? []).map((x) => [x.id, x]))
    const proiect = (id: string) => p.get(id)
    const versiune = (x: VersiuneCatalog) => `${tipuriVersiune[x.tip as keyof typeof tipuriVersiune] ?? x.tip} nr. ${x.numar} · ${x.denumire}`
    return { proiect, versiune, versiuni: v, lista: versiuni.data ?? [], seIncarca: versiuni.isLoading }
  }, [proiecte.data, versiuni.data, versiuni.isLoading])
}

/** Cheia resursei: codul fără spații, cu majuscule + UM cu litere mici (ca pe server, `CatalogPreturi.Cheie`). */
const cheieRand = (cod: string, um: string) => `${cod.trim().replace(/\s+/g, ' ').toUpperCase()}|${um.trim().replace(/\s+/g, ' ').toLowerCase()}`

/** Recepțiile materialului din stoc legat de resursă, cu prețul adus în UM-ul resursei (D-55). */
function AchizitiiResursa({ rand }: { rand: StatisticaPret }) {
  const { data, isLoading } = useDetaliiAchizitii(rand)
  const et = useEtichete()
  if (!isLoading && !data?.length) return null
  const primul = data?.[0]
  return (
    <div style={{ marginTop: 8 }}>
      <Typography.Text strong>Achiziții</Typography.Text>
      {primul && (
        <Typography.Text type="secondary" style={{ marginInlineStart: 8 }}>
          material din stoc: {primul.materialStoc}{primul.factor !== 1 ? ` · 1 ${rand.unitateMasura} = ${formatCantitate(primul.factor)} UM stoc` : ''}
        </Typography.Text>
      )}
      <Table<AchizitieResursa> className="tabel-compact" size="small" loading={isLoading} dataSource={data} pagination={false}
        rowKey={(a) => `${a.documentId}-${a.data}-${a.pret}-${a.cantitate}`}
        columns={[
          { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
          { title: 'Document', dataIndex: 'document', ellipsis: true },
          { title: 'Furnizor', dataIndex: 'furnizor', ellipsis: true },
          {
            title: 'Proiect', width: 200, ellipsis: true,
            render: (_, a) => { const p = a.proiectId ? et.proiect(a.proiectId) : undefined; return p ? `${p.cod} · ${p.denumire}` : '—' },
          },
          { title: `Cantitate (${rand.unitateMasura})`, dataIndex: 'cantitate', width: 120, className: 'numeric', render: formatCantitate },
          { title: `Preț / ${rand.unitateMasura}`, dataIndex: 'pret', width: 110, className: 'numeric', render: (v: number) => <b>{formatPret(v)}</b> },
        ]} />
    </div>
  )
}

/** Prețurile resursei / articolului în fiecare deviz (rândul desfăcut). */
function IstoricPreturi({ ce, rand, toate }: { ce: 'resurse' | 'articole'; rand: StatisticaPret; toate: boolean }) {
  const { data, isLoading } = usePreturi(ce, rand, toate)
  const et = useEtichete()
  const coloane: TableColumnsType<ObservatiePret> = [
    {
      title: 'Proiect',
      width: 220,
      ellipsis: true,
      render: (_, o) => {
        const p = et.proiect(o.proiectId)
        return p ? <Link to={`/proiecte/${p.id}?tab=devize`}>{p.cod} · {p.denumire}</Link> : '—'
      },
    },
    {
      title: 'Deviz',
      ellipsis: true,
      render: (_, o) => {
        const v = et.versiuni.get(o.versiuneId)
        return v ? <Link to={`/proiecte/${o.proiectId}/devize/${v.id}`}>{et.versiune(v)}</Link> : '—'
      },
    },
    { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
    { title: 'Denumire în deviz', dataIndex: 'denumire', ellipsis: true },
    { title: 'Cantitate', dataIndex: 'cantitate', width: 110, className: 'numeric', render: formatCantitate },
    ...(ce === 'articole'
      ? (['material', 'manopera', 'utilaj', 'transport'] as const).map((k) => ({
          title: { material: 'Material', manopera: 'Manoperă', utilaj: 'Utilaj', transport: 'Transport' }[k],
          width: 100,
          className: 'numeric',
          render: (_: unknown, o: ObservatiePret) => (o.componente && o.componente[k] ? formatPret(o.componente[k]) : ''),
        }))
      : []),
    { title: 'Preț unitar', dataIndex: 'pret', width: 110, className: 'numeric', render: (v: number) => <b>{formatPret(v)}</b> },
  ]
  return <Table<ObservatiePret> className="tabel-compact" rowKey={(o) => `${o.versiuneId}-${o.pret}-${o.cantitate}`} size="small" loading={isLoading}
    dataSource={data} columns={coloane} pagination={false} />
}

/**
 * Nomenclatorul din devize (D-54): resursele (materiale, manoperă, utilaje, transport) sau articolele (activitățile), cu
 * prețurile lor din toate devizele firmei; rândul se desface în prețurile din fiecare deviz.
 */
export function PreturiCatalog({ ce }: { ce: 'resurse' | 'articole' }) {
  const [cauta, setCauta] = useState('')
  const [tip, setTip] = useState('')
  const [toate, setToate] = useState(false)
  const { data, isFetching, error } = useCatalog(ce, { cauta: cauta.trim(), tip: ce === 'resurse' ? tip : undefined, toate })
  // Prețurile reale de achiziție (recepții) ale resurselor legate de materiale din stoc (D-55).
  const achizitii = useAchizitii(ce === 'resurse')
  const dupaCheie = new Map<string, StatisticaAchizitie>((achizitii.data ?? []).map((a) => [cheieRand(a.cod, a.unitateMasura), a]))
  const achizitie = (r: StatisticaPret) => dupaCheie.get(cheieRand(r.cod, r.unitateMasura))

  const coloane: TableColumnsType<StatisticaPret> = [
    { title: ce === 'resurse' ? 'Cod' : 'Simbol', dataIndex: 'cod', width: 130, fixed: 'left', render: (v) => <Typography.Text code>{v}</Typography.Text>, sorter: (a, b) => a.cod.localeCompare(b.cod) },
    { title: 'Denumire', dataIndex: 'denumire', ellipsis: { showTitle: true }, sorter: (a, b) => a.denumire.localeCompare(b.denumire) },
    { title: 'UM', dataIndex: 'unitateMasura', width: 60 },
    ce === 'resurse'
      ? { title: 'Tip', dataIndex: 'categorie', width: 95, render: (t: string) => tipuriResursa[t] ?? t }
      : { title: 'Cap.', dataIndex: 'categorie', width: 55 },
    { title: 'Devize', dataIndex: 'devize', width: 70, className: 'numeric', sorter: (a, b) => a.devize - b.devize },
    { title: 'Proiecte', dataIndex: 'proiecte', width: 75, className: 'numeric', sorter: (a, b) => a.proiecte - b.proiecte },
    { title: 'Cantitate', dataIndex: 'cantitate', width: 105, className: 'numeric', render: formatCantitate, sorter: (a, b) => a.cantitate - b.cantitate },
    { title: 'Minim', dataIndex: 'minim', width: 100, className: 'numeric', render: formatPret, sorter: (a, b) => a.minim - b.minim },
    {
      title: <Tooltip title="Media ponderată cu cantitățile din devize">Medie</Tooltip>,
      dataIndex: 'medie', width: 100, className: 'numeric', render: (v: number) => <b>{formatPret(v)}</b>, sorter: (a, b) => a.medie - b.medie,
    },
    { title: 'Maxim', dataIndex: 'maxim', width: 100, className: 'numeric', render: formatPret, sorter: (a, b) => a.maxim - b.maxim },
    {
      title: 'Ultimul preț', width: 150, className: 'numeric', sorter: (a, b) => a.dataUltimului.localeCompare(b.dataUltimului),
      render: (_, r) => <>{formatPret(r.ultimul)} <Typography.Text type="secondary" style={{ fontSize: 11 }}>{formatData(r.dataUltimului)}</Typography.Text></>,
    },
    {
      title: <Tooltip title="Cât diferă prețurile între devize: (maxim − minim) / minim">Abatere</Tooltip>,
      dataIndex: 'abatere', width: 85, className: 'numeric', render: (v: number | null) => <TagAbatere v={v} />,
      sorter: (a, b) => (a.abatere ?? -1) - (b.abatere ?? -1),
    },
    ...(ce === 'resurse' ? [
      {
        title: <Tooltip title="Ultimul preț de achiziție (recepție) al materialului din stoc legat de resursă, adus în UM-ul resursei. Resursele nelegate nu au: leagă-le în „Nomenclator resurse”.">Achiziție: ultimul</Tooltip>,
        width: 150, className: 'numeric',
        render: (_: unknown, r: StatisticaPret) => {
          const a = achizitie(r)
          if (!a) return <Typography.Text type="secondary">—</Typography.Text>
          return (
            <Tooltip title={`${a.documentUltim}${a.furnizorUltim ? ` · ${a.furnizorUltim}` : ''}`}>
              {formatPret(a.ultimul)} <Typography.Text type="secondary" style={{ fontSize: 11 }}>{formatData(a.dataUltimului)}</Typography.Text>
            </Tooltip>
          )
        },
      },
      {
        title: <Tooltip title="Media ponderată cu cantitățile a recepțiilor din ultimele 6 luni; în paranteză, diferența față de media din devize.">Achiziție: medie 6 luni</Tooltip>,
        width: 150, className: 'numeric',
        render: (_: unknown, r: StatisticaPret) => {
          const a = achizitie(r)
          if (!a?.medieInterval) return <Typography.Text type="secondary">—</Typography.Text>
          const dif = r.medie > 0 ? (a.medieInterval - r.medie) / r.medie : null
          return (
            <>
              <b>{formatPret(a.medieInterval)}</b>
              {dif !== null && <Typography.Text type={dif > 0.05 ? 'danger' : dif < -0.05 ? 'success' : 'secondary'} style={{ fontSize: 11, marginInlineStart: 4 }}>
                ({dif > 0 ? '+' : ''}{formatProcent(dif)})
              </Typography.Text>}
            </>
          )
        },
      },
    ] : []),
  ]

  const exporta = () =>
    data && descarcaFisier(csv([
      [ce === 'resurse' ? 'Cod' : 'Simbol', 'Denumire', 'UM', ce === 'resurse' ? 'Tip' : 'Capitol', 'Devize', 'Proiecte', 'Cantitate', 'Minim', 'Medie', 'Maxim', 'Ultimul', 'Data ultimului', 'Abatere'],
      ...data.map((r) => [r.cod, r.denumire, r.unitateMasura, ce === 'resurse' ? (tipuriResursa[r.categorie] ?? r.categorie) : r.categorie, r.devize, r.proiecte,
        r.cantitate, r.minim, r.medie, r.maxim, r.ultimul, formatData(r.dataUltimului), r.abatere]),
    ]), `Preturi_${ce}.csv`)

  return (
    <Card
      styles={{ body: { padding: 0 } }}
      title={
        <Space wrap>
          <Input.Search allowClear placeholder={ce === 'resurse' ? 'Caută cod sau denumire' : 'Caută simbol sau denumire'} style={{ width: 280 }}
            onSearch={setCauta} onChange={(e) => !e.target.value && setCauta('')} />
          {ce === 'resurse' && (
            <Segmented value={tip} onChange={setTip}
              options={[{ value: '', label: 'Toate' }, ...Object.entries(tipuriResursa).map(([value, label]) => ({ value, label }))]} />
          )}
        </Space>
      }
      extra={
        <Space>
          <Tooltip title="Implicit intră doar versiunile în vigoare (nu cele înlocuite și nici ciornele de lucru, care repetă devizul din care pornesc).">
            <Checkbox checked={toate} onChange={(e) => setToate(e.target.checked)}>și versiunile înlocuite</Checkbox>
          </Tooltip>
          <Typography.Text type="secondary">{data?.length ?? 0} {ce === 'resurse' ? 'resurse' : 'articole'}</Typography.Text>
          <Tooltip title="Export CSV"><DownloadOutlined onClick={exporta} style={{ cursor: 'pointer' }} /></Tooltip>
        </Space>
      }
    >
      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ margin: 12 }} />}
      <Table<StatisticaPret>
        className="tabel-compact"
        rowKey={(r) => `${r.cod}|${r.unitateMasura}`}
        size="small"
        loading={isFetching}
        dataSource={data}
        columns={coloane}
        sticky
        scroll={{ x: ce === 'resurse' ? 1600 : 1300 }}
        pagination={{ pageSize: 50, showSizeChanger: true, pageSizeOptions: [50, 100, 200], size: 'small', hideOnSinglePage: true }}
        expandable={{ expandedRowRender: (r) => <><IstoricPreturi ce={ce} rand={r} toate={toate} />{ce === 'resurse' && <AchizitiiResursa rand={r} />}</> }}
        locale={{ emptyText: <Empty description="Niciun preț: importă devize în proiecte ca să se adune aici." /> }}
      />
    </Card>
  )
}

/** Compararea prețurilor între devize (din același proiect sau din proiecte diferite). */
export function ComparatieDevize() {
  const et = useEtichete()
  const { token } = theme.useToken()
  const [alese, setAlese] = useState<string[]>([])
  const [ce, setCe] = useState<'articole' | 'resurse'>('articole')
  const [doarComune, setDoarComune] = useState(false)
  const [cauta, setCauta] = useState('')
  const { data, isFetching, error } = useComparatie(alese, ce)

  const optiuni = useMemo(() => {
    const grupuri = new Map<string, VersiuneCatalog[]>()
    for (const v of et.lista) grupuri.set(v.proiectId, [...(grupuri.get(v.proiectId) ?? []), v])
    return [...grupuri.entries()].map(([proiectId, vs]) => {
      const p = et.proiect(proiectId)
      return {
        label: p ? `${p.cod} · ${p.denumire}` : 'Proiect',
        title: proiectId,
        options: vs.map((v) => ({
          value: v.id,
          label: `${p?.cod ?? ''} · ${et.versiune(v)} (${stariVersiune[v.stare].eticheta.toLowerCase()})`,
        })),
      }
    })
  }, [et])

  const t = cauta.trim().toLowerCase()
  const randuri = (data?.randuri ?? []).filter((r) => (!doarComune || r.inToate) && (!t || r.cod.toLowerCase().includes(t) || r.denumire.toLowerCase().includes(t)))

  const coloane: TableColumnsType<RandComparatie> = [
    { title: ce === 'articole' ? 'Simbol' : 'Cod', dataIndex: 'cod', width: 120, fixed: 'left', render: (v) => <Typography.Text code>{v}</Typography.Text> },
    { title: 'Denumire', dataIndex: 'denumire', width: 320, ellipsis: { showTitle: true } },
    { title: 'UM', dataIndex: 'unitateMasura', width: 60 },
    ...(data?.versiuni ?? []).map((v, i) => ({
      key: v.id,
      title: (
        <Tooltip title={`${et.proiect(v.proiectId)?.denumire ?? ''} · ${et.versiune(v)}`}>
          <div style={{ whiteSpace: 'normal', lineHeight: 1.2 }}>
            <div>{et.proiect(v.proiectId)?.cod}</div>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>{tipuriVersiune[v.tip as keyof typeof tipuriVersiune] ?? v.tip} nr. {v.numar}</Typography.Text>
          </div>
        </Tooltip>
      ),
      width: 115,
      className: 'numeric',
      sorter: (a: RandComparatie, b: RandComparatie) => (a.preturi[i] ?? -1) - (b.preturi[i] ?? -1),
      render: (_: unknown, r: RandComparatie) => {
        const p = r.preturi[i]
        if (p === null) return <Typography.Text type="secondary">—</Typography.Text>
        const prezente = r.preturi.filter((x): x is number => x !== null)
        const extrem = prezente.length > 1 && Math.min(...prezente) !== Math.max(...prezente)
        const culoare = extrem && p === Math.min(...prezente) ? token.colorSuccess : extrem && p === Math.max(...prezente) ? token.colorError : undefined
        return <span style={{ color: culoare, fontWeight: culoare ? 600 : undefined }}>{formatPret(p)}</span>
      },
    })),
    {
      title: 'Abatere', dataIndex: 'abatere', width: 85, className: 'numeric', render: (v: number | null) => <TagAbatere v={v} />,
      sorter: (a, b) => (a.abatere ?? -1) - (b.abatere ?? -1), defaultSortOrder: 'descend',
    },
  ]

  return (
    <Flex vertical gap={12}>
      <Card size="small">
        <Flex wrap gap={12} align="center">
          <Select mode="multiple" allowClear showSearch optionFilterProp="label" style={{ minWidth: 420, flex: 1 }} maxCount={10}
            placeholder="Alege 2–10 devize de comparat (din orice proiect)" loading={et.seIncarca} value={alese} onChange={setAlese} options={optiuni} />
          <Segmented value={ce} onChange={setCe} options={[{ value: 'articole', label: 'Articole' }, { value: 'resurse', label: 'Resurse' }]} />
          <Space size={4}><Switch size="small" checked={doarComune} onChange={setDoarComune} /><span>Doar cele din toate devizele</span></Space>
          <Input.Search allowClear placeholder="Filtrează" style={{ width: 200 }} onChange={(e) => setCauta(e.target.value)} />
        </Flex>
      </Card>
      {error && <Alert type="error" showIcon title={(error as Error).message} />}
      {alese.length < 2 ? (
        <Card><Empty description="Alege cel puțin două devize: prețurile aceluiași articol (simbol + UM) sau ale aceleiași resurse (cod + UM) apar una lângă alta, cu minimul verde și maximul roșu." /></Card>
      ) : (
        <Card size="small" styles={{ body: { padding: 0 } }}
          title={`${randuri.length} ${ce === 'articole' ? 'articole' : 'resurse'}${data ? ` · ${data.randuri.filter((r) => r.inToate).length} comune tuturor devizelor` : ''}`}>
          <Table<RandComparatie> className="tabel-compact" rowKey={(r) => `${r.cod}|${r.unitateMasura}`} size="small" loading={isFetching}
            dataSource={randuri} columns={coloane} sticky scroll={{ x: 'max-content' }}
            pagination={{ pageSize: 100, showSizeChanger: true, size: 'small', hideOnSinglePage: true }} />
        </Card>
      )}
    </Flex>
  )
}
