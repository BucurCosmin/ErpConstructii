import { DeleteOutlined, DiffOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Empty, Input, Popconfirm, Progress, Row, Segmented, Select, Skeleton, Space, Statistic, Table, Tag, Tooltip, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { usePermisiuni } from '../../api/interogari'
import { fisierGrafic, useContinutGrafic, useCosturiGrafic, useGrafice, useStergeVersiuneGrafic } from '../../api/grafic'
import type { ActivitateGrafic, ContinutGrafic, CostTinta, NodWbs } from '../../api/grafic'
import { formatData, formatLei } from '../../util/format'
import { descarcaFisier } from '../../util/pdf'
import { AlocareCosturi } from './AlocareCosturi'
import { ComparatieGrafic } from './ComparatieGrafic'
import { DetaliuActivitate } from './DetaliuActivitate'
import { ImportGrafic, zile } from './ImportGrafic'

/** Rândul arborelui: un nod WBS (cu copiii lui) sau o activitate. */
interface Rand {
  cheie: string
  nod?: NodWbs
  activitate?: ActivitateGrafic
  adancime: number
  children?: Rand[]
}

type Filtru = 'toate' | 'neterminate' | 'in-curs' | 'critice' | 'intarziate'

/** Activitatea neterminată care se termină după data planificată. */
const intarziata = (a: ActivitateGrafic) => a.stadiu !== 'Finalizata' && !!a.sfarsit && a.sfarsit > a.sfarsitPlanificat

const zi = (d: string) => dayjs(d).startOf('day').valueOf()

/** Intervalul desenat în coloana Gantt și reperele lunare / trimestriale din antet. */
function useInterval(c: ContinutGrafic | undefined) {
  return useMemo(() => {
    if (!c) return null
    const date = [c.total.start, c.total.startPlanificat, c.total.sfarsit, c.total.sfarsitPlanificat, c.versiune.dataActualizare].filter(Boolean) as string[]
    if (date.length === 0) return null
    const min = dayjs(date.reduce((a, b) => (a < b ? a : b))).startOf('month')
    const max = dayjs(date.reduce((a, b) => (a > b ? a : b))).endOf('month')
    const luni = max.diff(min, 'month') + 1
    const pas = luni > 30 ? 6 : luni > 14 ? 3 : 1
    const repere: { la: number; text: string }[] = []
    for (let d = min; d.isBefore(max); d = d.add(pas, 'month')) repere.push({ la: d.valueOf(), text: pas === 1 ? d.format('MM.YY') : d.format('MM.YYYY') })
    return { min: min.valueOf(), max: max.valueOf(), repere }
  }, [c])
}

/** Bara Gantt a unui rând: planificat (subțire, gri) și actual / prognozat (cu partea executată plină). */
function Bara({ interval, start, sfarsit, startPlan, sfarsitPlan, procent, culoare, jalon, rezumat, dataActualizare }: {
  interval: { min: number; max: number }
  start: string | null
  sfarsit: string | null
  startPlan: string | null
  sfarsitPlan: string | null
  procent: number
  culoare: string
  jalon?: boolean
  rezumat?: boolean
  dataActualizare: string | null
}) {
  const latime = interval.max - interval.min
  const poz = (d: string) => ((zi(d) - interval.min) / latime) * 100
  const lung = (a: string, b: string) => Math.max(((zi(b) - zi(a) + 86_400_000) / latime) * 100, 0.3)
  return (
    <div style={{ position: 'relative', height: 16 }}>
      {dataActualizare && <div style={{ position: 'absolute', left: `${poz(dataActualizare)}%`, top: -4, bottom: -4, borderLeft: '1px dashed #f5222d', opacity: 0.6 }} />}
      {startPlan && sfarsitPlan && !jalon && (
        <div style={{ position: 'absolute', left: `${poz(startPlan)}%`, width: `${lung(startPlan, sfarsitPlan)}%`, bottom: 0, height: 3, background: '#bfbfbf' }} />
      )}
      {start && sfarsit && (jalon ? (
        <div style={{ position: 'absolute', left: `calc(${poz(sfarsit)}% - 5px)`, top: 2, width: 9, height: 9, background: culoare, transform: 'rotate(45deg)' }} />
      ) : (
        <div style={{
          position: 'absolute', left: `${poz(start)}%`, width: `${lung(start, sfarsit)}%`, top: rezumat ? 4 : 1, height: rezumat ? 6 : 10,
          border: `1px solid ${culoare}`, background: `linear-gradient(90deg, ${culoare} ${procent}%, ${culoare}33 ${procent}%)`, borderRadius: 2,
        }} />
      ))}
    </div>
  )
}

/**
 * Graficul de execuție al proiectului (fișa proiectului → Grafic, D-57): actualizările importate din Primavera, structura WBS
 * cu totalurile ei, activitățile cu datele, procentul, rezerva și costurile, bare Gantt (planificat vs actual / prognozat,
 * data actualizării), filtre după stadiu și codurile de activitate, comparația cu actualizarea anterioară.
 */
export function GraficProiect({ proiectId }: { proiectId: string }) {
  const { message } = App.useApp()
  const { token } = theme.useToken()
  const grafice = useGrafice(proiectId)
  const permisiuni = usePermisiuni()
  const poateEdita = permisiuni.data?.includes('proiecte.editare') ?? false
  // Costurile ERP alocate pe grafic: ca la buget (D-57, etapa 2).
  const vedeCosturi = (permisiuni.data?.includes('buget.vizualizare') && permisiuni.data.includes('date.preturi_cost')) ?? false
  const poateAloca = vedeCosturi && (permisiuni.data?.includes('buget.editare') ?? false)
  const [vedere, setVedere] = useState<'grafic' | 'costuri'>('grafic')
  const sterge = useStergeVersiuneGrafic(proiectId)

  const [graficId, setGraficId] = useState<string>()
  const [versiuneId, setVersiuneId] = useState<string>()
  const [import_, setImport] = useState<{ graficId?: string }>()
  const [activitate, setActivitate] = useState<string>()
  const [comparatie, setComparatie] = useState(false)
  const [cauta, setCauta] = useState('')
  const [filtru, setFiltru] = useState<Filtru>('toate')
  const [cod, setCod] = useState<string>()
  const [deschise, setDeschise] = useState<string[]>()

  const lista = grafice.data ?? []
  // Graficul ales (după un import nou: cel care conține versiunea importată), altfel primul.
  const grafic = lista.find((g) => g.id === graficId) ?? lista.find((g) => g.versiuni.some((v) => v.id === versiuneId)) ?? lista[0]
  const versiune = grafic?.versiuni.find((v) => v.id === versiuneId) ?? grafic?.versiuni.find((v) => v.id === grafic.versiuneCurentaId) ?? grafic?.versiuni[0]
  useEffect(() => {
    if (grafic && grafic.id !== graficId) setGraficId(grafic.id)
  }, [grafic, graficId])
  const continut = useContinutGrafic(proiectId, grafic?.id, versiune?.id)
  // Țintele alocării vin din actualizarea curentă (aceeași cerere când e și cea afișată).
  const continutCurent = useContinutGrafic(proiectId, grafic?.id, grafic?.versiuneCurentaId ?? undefined)
  const costuri = useCosturiGrafic(proiectId, grafic?.id, vedeCosturi)
  const k = costuri.data
  const c = continut.data
  const interval = useInterval(c)
  const moneda = grafic?.moneda ?? ''

  const filtrat = !!cauta.trim() || filtru !== 'toate' || !!cod

  const { arbore, cheiNiveluri, numarActivitati } = useMemo(() => {
    if (!c) return { arbore: [] as Rand[], cheiNiveluri: [] as string[][], numarActivitati: 0 }
    const t = cauta.trim().toLowerCase()
    const [tipCod, valoareCod] = cod ? cod.split('\u0001') : []
    const pastreaza = (a: ActivitateGrafic) =>
      (!t || a.cod.toLowerCase().includes(t) || a.denumire.toLowerCase().includes(t))
      && (!tipCod || a.coduri[tipCod] === valoareCod)
      && (filtru === 'toate'
        || (filtru === 'neterminate' && a.stadiu !== 'Finalizata')
        || (filtru === 'in-curs' && a.stadiu === 'InCurs')
        || (filtru === 'critice' && a.critica)
        || (filtru === 'intarziate' && intarziata(a)))
    const activitati = c.activitati.filter(pastreaza)
    const peNod = new Map<string | null, ActivitateGrafic[]>()
    for (const a of activitati) peNod.set(a.cheieWbs, [...(peNod.get(a.cheieWbs) ?? []), a])
    const copii = new Map<string | null, NodWbs[]>()
    for (const n of c.noduri) copii.set(n.cheieParinte, [...(copii.get(n.cheieParinte) ?? []), n])
    const niveluri: string[][] = []
    const construieste = (parinte: string | null, adancime: number): Rand[] => {
      const noduri = (copii.get(parinte) ?? []).flatMap((n): Rand[] => {
        const sub = construieste(n.cheie, adancime + 1)
        // Cu filtru, nodurile fără activități potrivite dispar.
        if (filtrat && sub.length === 0) return []
        ;(niveluri[adancime] ??= []).push(`w:${n.cheie}`)
        return [{ cheie: `w:${n.cheie}`, nod: n, adancime, children: sub.length > 0 ? sub : undefined }]
      })
      const acts = (peNod.get(parinte) ?? []).map((a): Rand => ({ cheie: `a:${a.cod}`, activitate: a, adancime }))
      return [...noduri, ...acts]
    }
    return { arbore: construieste(null, 0), cheiNiveluri: niveluri, numarActivitati: activitati.length }
  }, [c, cauta, filtru, cod, filtrat])

  // Implicit: primele două niveluri deschise; cu filtru, tot.
  const nivelDeschis = (n: number) => cheiNiveluri.slice(0, n).flat()
  const randuriDeschise = filtrat ? cheiNiveluri.flat() : (deschise ?? nivelDeschis(2))

  const descarca = async () => {
    if (!grafic || !versiune) return
    try {
      descarcaFisier(await fisierGrafic(proiectId, grafic.id, versiune.id), versiune.numeFisier)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const stergeVersiunea = async () => {
    if (!grafic || !versiune) return
    try {
      await sterge.mutateAsync({ graficId: grafic.id, versiuneId: versiune.id })
      message.success(grafic.versiuni.length === 1 ? 'Graficul a fost șters.' : `Actualizarea nr. ${versiune.numar} a fost ștearsă.`)
      setVersiuneId(undefined)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const costErp = (r: Rand): CostTinta | undefined => (r.nod ? k?.peNod[r.nod.cheie] : k?.peActivitate[r.activitate!.cod])
  const deAlocat = k?.documente.filter((s) => s.stare === 'Nealocat' || s.stare === 'Partial').length ?? 0

  const culoare = (a: ActivitateGrafic) =>
    a.stadiu === 'Finalizata' ? token.colorSuccess : a.critica ? token.colorError : intarziata(a) ? token.colorWarning : token.colorPrimary

  const coloane: TableColumnsType<Rand> = [
    {
      title: 'Cod / WBS', key: 'cod', width: 250, fixed: 'left',
      render: (_, r) => (r.nod ? <b>{r.nod.cod}</b> : <Typography.Link onClick={() => setActivitate(r.activitate!.cod)}>{r.activitate!.cod}</Typography.Link>),
    },
    {
      title: 'Denumire', key: 'denumire', width: 380, ellipsis: { showTitle: false },
      render: (_, r) => {
        if (r.nod)
          return <Tooltip title={r.nod.denumire} placement="topLeft"><b>{r.nod.denumire}</b> <Typography.Text type="secondary">({r.nod.total.activitati})</Typography.Text></Tooltip>
        const a = r.activitate!
        return (
          <Tooltip title={a.denumire} placement="topLeft">
            {a.tip === 'JalonInceput' || a.tip === 'JalonSfarsit' ? '◆ ' : ''}{a.denumire}
            {a.critica && <Tag color="red" style={{ marginLeft: 4, fontSize: 11, lineHeight: '16px' }}>critică</Tag>}
          </Tooltip>
        )
      },
    },
    {
      title: 'Start', key: 'start', width: 90,
      render: (_, r) => formatData(r.nod ? r.nod.total.start : r.activitate!.start),
    },
    {
      title: 'Sfârșit', key: 'sfarsit', width: 90,
      render: (_, r) => {
        if (r.nod) return formatData(r.nod.total.sfarsit)
        const a = r.activitate!
        return intarziata(a)
          ? <Tooltip title={`Planificat: ${formatData(a.sfarsitPlanificat)}`}><Typography.Text type="warning">{formatData(a.sfarsit)}</Typography.Text></Tooltip>
          : formatData(a.sfarsit)
      },
    },
    { title: 'Durată', key: 'durata', width: 65, className: 'numeric', render: (_, r) => (r.activitate ? `${r.activitate.durataZile.toLocaleString('ro-RO')} z` : '') },
    {
      title: 'Execuție', key: 'procent', width: 110,
      render: (_, r) => {
        const p = r.nod ? r.nod.total.procent : r.activitate!.procent
        return <Progress percent={p} size="small" format={(x) => `${(x ?? 0).toLocaleString('ro-RO', { maximumFractionDigits: 1 })}%`} style={{ margin: 0 }} />
      },
    },
    {
      title: <Tooltip title="Rezerva totală (total float), în zile lucrătoare">Rezervă</Tooltip>, key: 'rezerva', width: 70, className: 'numeric',
      render: (_, r) => (r.activitate?.rezervaTotalaZile ?? null) === null ? '' : <Typography.Text type={r.activitate!.critica ? 'danger' : undefined}>{r.activitate!.rezervaTotalaZile!.toLocaleString('ro-RO')}</Typography.Text>,
    },
    {
      title: `Cost plan. (${moneda})`, key: 'cost', width: 125, className: 'numeric',
      render: (_, r) => { const v = r.nod ? r.nod.total.costPlanificat : r.activitate!.costPlanificat; return v ? formatLei(v) : '' },
    },
    {
      title: `Realizat (${moneda})`, key: 'realizat', width: 125, className: 'numeric',
      render: (_, r) => { const v = r.nod ? r.nod.total.costRealizat : r.activitate!.costRealizat; return v ? formatLei(v) : '' },
    },
    ...(vedeCosturi
      ? [
          {
            title: <Tooltip title="Facturile și bonurile de consum alocate pe activitate / nod (cu tot ce e dedesubt), în lei">Cost ERP (lei)</Tooltip>,
            key: 'erp-lei', width: 120, className: 'numeric',
            render: (_: unknown, r: Rand) => { const x = costErp(r); return x ? formatLei(x.lei) : '' },
          },
          {
            title: <Tooltip title="Același cost, la cursul BNR din data fiecărui document">{`Cost ERP (${moneda})`}</Tooltip>,
            key: 'erp-grafic', width: 120, className: 'numeric',
            render: (_: unknown, r: Rand) => {
              const x = costErp(r)
              if (!x) return ''
              return x.monedaGrafic === null ? <Tooltip title={`${x.faraCurs} alocări fără curs BNR`}>—</Tooltip> : formatLei(x.monedaGrafic)
            },
          },
        ]
      : []),
    {
      key: 'gantt', width: 480,
      title: interval ? (
        <div style={{ position: 'relative', height: 18 }}>
          {interval.repere.map((x) => (
            <span key={x.la} style={{ position: 'absolute', left: `${((x.la - interval.min) / (interval.max - interval.min)) * 100}%`, fontSize: 11, fontWeight: 400, borderLeft: '1px solid #d9d9d9', paddingLeft: 2 }}>
              {x.text}
            </span>
          ))}
        </div>
      ) : 'Gantt',
      render: (_, r) => {
        if (!interval || !c) return null
        if (r.nod)
          return <Bara interval={interval} start={r.nod.total.start} sfarsit={r.nod.total.sfarsit} startPlan={r.nod.total.startPlanificat}
            sfarsitPlan={r.nod.total.sfarsitPlanificat} procent={r.nod.total.procent} culoare={token.colorTextSecondary} rezumat dataActualizare={c.versiune.dataActualizare} />
        const a = r.activitate!
        return <Bara interval={interval} start={a.start} sfarsit={a.sfarsit} startPlan={a.startPlanificat} sfarsitPlan={a.sfarsitPlanificat} procent={a.procent}
          culoare={culoare(a)} jalon={a.tip === 'JalonInceput' || a.tip === 'JalonSfarsit'} dataActualizare={c.versiune.dataActualizare} />
      },
    },
  ]

  if (grafice.isLoading) return <Skeleton active />
  if (lista.length === 0)
    return (
      <>
        <Empty description="Proiectul nu are încă un grafic de execuție. Importă exportul Primavera (XER).">
          {poateEdita && <Button type="primary" icon={<UploadOutlined />} onClick={() => setImport({})}>Importă din Primavera</Button>}
        </Empty>
        <ImportGrafic proiectId={proiectId} grafice={lista} deschis={!!import_} laInchidere={(r) => { setImport(undefined); if (r) setVersiuneId(r.versiuneId) }} />
      </>
    )

  const rezumat = versiune?.comparatie
  const ultima = grafic && versiune && grafic.versiuni[0]?.id === versiune.id
  const optiuniCod = [...new Set(c?.coduri.map((x) => x.tip))].map((tip) => ({
    label: tip,
    options: (c?.coduri ?? []).filter((x) => x.tip === tip).map((x) => ({ value: `${tip}\u0001${x.cod}`, label: `${x.cod} — ${x.denumire}` })),
  }))

  return (
    <Space orientation="vertical" size={12} style={{ width: '100%' }}>
      <Space wrap>
        {lista.length > 1 && (
          <Select style={{ minWidth: 260 }} value={grafic?.id} onChange={(id) => { setGraficId(id); setVersiuneId(undefined); setDeschise(undefined) }}
            options={lista.map((g) => ({ value: g.id, label: g.denumire }))} />
        )}
        <Select style={{ minWidth: 260 }} value={versiune?.id} onChange={(id) => setVersiuneId(id)}
          options={grafic?.versiuni.map((v) => ({ value: v.id, label: `Actualizarea nr. ${v.numar} · ${formatData(v.dataActualizare)}${v.id === grafic.versiuneCurentaId ? ' (curentă)' : ''}` }))} />
        {poateEdita && <Button type="primary" icon={<UploadOutlined />} onClick={() => setImport({ graficId: grafic?.id })}>Importă actualizare (XER)</Button>}
        {versiune && versiune.numar > 1 && <Button icon={<DiffOutlined />} onClick={() => setComparatie(true)}>Ce s-a schimbat</Button>}
        <Button icon={<DownloadOutlined />} onClick={() => void descarca()}>Fișierul original</Button>
        {poateEdita && ultima && (
          <Popconfirm title={grafic.versiuni.length === 1 ? 'Ștergi graficul (are o singură actualizare)?' : `Ștergi actualizarea nr. ${versiune.numar}?`}
            description="Folosește doar pentru un import greșit." okText="Șterge" okButtonProps={{ danger: true }} cancelText="Nu" onConfirm={() => void stergeVersiunea()}>
            <Button danger icon={<DeleteOutlined />}>Șterge actualizarea</Button>
          </Popconfirm>
        )}
      </Space>

      {versiune && (
        <Row gutter={[12, 12]}>
          <Col xs={12} md={4}><Card size="small"><Statistic title="Data actualizării" value={formatData(versiune.dataActualizare)} /></Card></Col>
          <Col xs={12} md={5}><Card size="small"><Statistic title="Perioada" value={`${formatData(versiune.start)} – ${formatData(versiune.sfarsit)}`} styles={{ content: { fontSize: 18 } }} /></Card></Col>
          <Col xs={12} md={4}>
            <Card size="small"><Statistic title="Execuție (ponderată cu costul)" value={versiune.procent} precision={2} suffix="%" /></Card>
          </Col>
          <Col xs={12} md={5}>
            <Card size="small">
              <Statistic title="Activități" value={versiune.activitati} suffix={<Typography.Text type="secondary" style={{ fontSize: 13 }}>
                {versiune.finalizate} finalizate · {versiune.inCurs} în curs · <Typography.Text type="danger">{versiune.critice} critice</Typography.Text></Typography.Text>} />
            </Card>
          </Col>
          <Col xs={24} md={6}>
            <Card size="small">
              <Statistic title={`Cost planificat / realizat (${moneda})`} value={formatLei(versiune.costPlanificat)} styles={{ content: { fontSize: 18 } }}
                suffix={<Typography.Text type="secondary" style={{ fontSize: 13 }}>/ {formatLei(versiune.costRealizat)}</Typography.Text>} />
            </Card>
          </Col>
        </Row>
      )}

      {rezumat && (
        <Alert type={rezumat.decalajSfarsitProiectZile && rezumat.decalajSfarsitProiectZile > 0 ? 'warning' : 'info'} showIcon
          title={<>Față de actualizarea nr. {versiune!.numar - 1}: sfârșitul {rezumat.decalajSfarsitProiectZile ? <b>{zile(rezumat.decalajSfarsitProiectZile)}</b> : 'neschimbat'},
            execuția {rezumat.procentAnterior}% → {rezumat.procent}%, {rezumat.finalizate} activități finalizate, {rezumat.intarziate} cu sfârșitul amânat,
            {' '}{rezumat.noi} noi, {rezumat.sterse} șterse.</>}
          action={<Button size="small" onClick={() => setComparatie(true)}>Detalii</Button>} />
      )}

      {vedeCosturi && (
        <Space wrap>
          <Segmented value={vedere} onChange={(v) => setVedere(v as 'grafic' | 'costuri')}
            options={[{ value: 'grafic', label: 'Grafic' }, { value: 'costuri', label: `Alocare costuri${deAlocat ? ` (${deAlocat} de alocat)` : ''}` }]} />
          {k && (
            <Typography.Text type="secondary">
              Costuri ERP alocate: <b>{formatLei(k.total.lei)} lei</b>
              {k.total.monedaGrafic !== null && <> = <b>{formatLei(k.total.monedaGrafic)} {moneda}</b></>}
            </Typography.Text>
          )}
        </Space>
      )}

      {vedere === 'costuri' && vedeCosturi && grafic ? (
        k ? <AlocareCosturi proiectId={proiectId} graficId={grafic.id} costuri={k} continutCurent={continutCurent.data} poateEdita={poateAloca} /> : <Skeleton active />
      ) : (<>
      <Space wrap>
        <Input.Search allowClear placeholder="Caută cod sau denumire" style={{ width: 260 }} onChange={(e) => setCauta(e.target.value)} />
        <Segmented<Filtru> value={filtru} onChange={setFiltru}
          options={[
            { value: 'toate', label: 'Toate' },
            { value: 'neterminate', label: 'Neterminate' },
            { value: 'in-curs', label: 'În curs' },
            { value: 'critice', label: 'Critice' },
            { value: 'intarziate', label: 'Întârziate' },
          ]} />
        {optiuniCod.length > 0 && (
          <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Cod de activitate (zonă, subantreprenor, …)" style={{ width: 340 }}
            value={cod} onChange={setCod} options={optiuniCod} />
        )}
        {!filtrat && (
          <Space.Compact>
            {[1, 2, 3].map((n) => <Button key={n} size="small" onClick={() => setDeschise(nivelDeschis(n))}>Nivel {n}</Button>)}
            <Button size="small" onClick={() => setDeschise(cheiNiveluri.flat())}>Tot</Button>
            <Button size="small" onClick={() => setDeschise([])}>Restrânge</Button>
          </Space.Compact>
        )}
        {filtrat && <Typography.Text type="secondary">{numarActivitati} activități</Typography.Text>}
      </Space>

      {continut.isLoading || !c ? <Skeleton active /> : (
        <Table<Rand> className="tabel-compact" size="small" rowKey="cheie" dataSource={arbore} columns={coloane} pagination={false}
          scroll={{ x: vedeCosturi ? 2040 : 1800, y: 'calc(100vh - 330px)' }} sticky
          expandable={{
            expandedRowKeys: randuriDeschise,
            onExpandedRowsChange: (k) => setDeschise(k as string[]),
            indentSize: 14,
          }}
          onRow={(r) => ({
            onClick: () => { if (r.activitate) setActivitate(r.activitate.cod) },
            style: { cursor: r.activitate ? 'pointer' : undefined, background: r.nod && r.adancime === 0 ? token.colorFillQuaternary : undefined },
          })} />
      )}
      </>)}

      {grafic && versiune && (
        <>
          <DetaliuActivitate proiectId={proiectId} graficId={grafic.id} versiuneId={versiune.id} cod={activitate} moneda={moneda}
            costuriErp={vedeCosturi ? (k?.alocari ?? []).filter((a) => a.tip === 'Activitate' && a.cheie === activitate) : undefined}
            laInchidere={() => setActivitate(undefined)} laAlegere={setActivitate} />
          <ComparatieGrafic proiectId={proiectId} graficId={grafic.id} versiuneId={comparatie ? versiune.id : undefined} numar={versiune.numar}
            laInchidere={() => setComparatie(false)} laAlegere={(x) => { setComparatie(false); setActivitate(x) }} />
        </>
      )}
      <ImportGrafic proiectId={proiectId} grafice={lista} graficId={import_?.graficId} deschis={!!import_}
        laInchidere={(r) => { setImport(undefined); if (r) { setGraficId(r.graficId || undefined); setVersiuneId(r.versiuneId) } }} />
    </Space>
  )
}
