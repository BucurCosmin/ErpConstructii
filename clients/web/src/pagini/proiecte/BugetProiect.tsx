import { MinusSquareOutlined, PlusSquareOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, Empty, Input, Modal, Progress, Row, Segmented, Select, Skeleton, Space, Statistic, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useBugetProiect, useCosturiBuget } from '../../api/buget'
import type { CostDocument, RandBuget } from '../../api/buget'
import { formatCantitate, formatData, formatLei, formatProcent, stariVersiune, tipuriVersiune } from '../../util/format'

/** Rând al tabelului: un capitol (cu sumele articolelor de sub el, pe toate nivelurile) sau un articol. */
interface RandTabel {
  cheie: string
  capitol?: string
  /** Nivelul capitolului în arbore (0 = primul nivel: obiectivul / obiectul). */
  nivel?: number
  /** Capitolul are capitole sau articole sub el (se poate închide). */
  areCopii?: boolean
  rand?: RandBuget
  buget: number
  bugetMaterial: number
  consumat: number
}

/** Un capitol din arborele bugetului, cu sumele articolelor de sub el. */
interface NodBuget {
  cheie: string
  denumire: string
  nivel: number
  copii: NodBuget[]
  articole: RandBuget[]
  buget: number
  bugetMaterial: number
  consumat: number
}

/** Separă denumirile capitolelor în cheia nodului (nu apare în denumiri). */
const SEPARATOR = '\u241f'

const naturi: Record<CostDocument['natura'], string> = { Consum: 'consum', AchizitieMateriale: 'materiale cumpărate', Serviciu: 'serviciu' }

/** Documentele cu costuri ale unui rând (articol, cheltuieli generale, servicii, materiale cumpărate). */
function DocumenteCost({ proiectId, articol }: { proiectId: string; articol: string }) {
  const { data, isLoading } = useCosturiBuget(proiectId, articol)
  return (
    <Table<CostDocument> className="tabel-compact tabel-text-complet" size="small" loading={isLoading} dataSource={data} pagination={data && data.length > 20 ? { pageSize: 20, size: 'small' } : false}
      rowKey={(c, i) => `${c.documentId}-${i}`} locale={{ emptyText: 'Niciun document' }}
      columns={[
        { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
        { title: 'Document', dataIndex: 'document', width: 260 },
        { title: 'Natura', dataIndex: 'natura', width: 140, render: (n: CostDocument['natura']) => naturi[n] },
        { title: 'Detalii', dataIndex: 'descriere' },
        { title: 'Valoare (lei)', dataIndex: 'valoare', width: 120, className: 'numeric', render: (v: number) => formatLei(v) },
      ]} />
  )
}

/** Procentul consumat din buget, cu culoare: peste 100% roșu, peste 90% portocaliu. */
function Consum({ buget, consumat }: { buget: number; consumat: number }) {
  if (buget <= 0) return consumat > 0 ? <Tag color="red">fără buget</Tag> : null
  const p = (consumat / buget) * 100
  return (
    <Tooltip title={`${formatLei(consumat)} din ${formatLei(buget)}`}>
      <Progress percent={Math.min(p, 100)} size="small" format={() => `${p.toFixed(0)}%`}
        status={p > 100 ? 'exception' : p > 90 ? 'active' : 'normal'} strokeColor={p > 100 ? undefined : p > 90 ? '#fa8c16' : undefined} />
    </Tooltip>
  )
}

/**
 * Bugetul proiectului (fișa proiectului → Buget): pe articolele devizului de referință, bugetul (cost direct pe resurse)
 * față de costurile reale, plus costurile proiectului care nu sunt încă pe articole (cheltuieli generale, servicii
 * facturate, materiale cumpărate și neconsumate).
 */
export function BugetProiect({ proiectId }: { proiectId: string }) {
  // Bugetul pe tot proiectul, pe un obiectiv sau pe un deviz anume (D-59): „” | „o:<id>” | „v:<id>”.
  const [alegere, setAlegere] = useState('')
  const { data, isLoading, error } = useBugetProiect(proiectId,
    alegere.startsWith('v:') ? { versiuneId: alegere.slice(2) } : alegere.startsWith('o:') ? { obiectivId: alegere.slice(2) } : {})
  const [cauta, setCauta] = useState('')
  const [filtru, setFiltru] = useState<'toate' | 'cu-costuri' | 'depasite'>('toate')
  const [documente, setDocumente] = useState<{ titlu: string; articol: string }>()
  // Capitolele închise din arbore (cheile lor); la căutare se arată tot.
  const [inchise, setInchise] = useState<Set<string>>(new Set())

  // Arborele capitolelor (obiectiv → obiect → capitole, pe oricâte niveluri), cu sumele pe fiecare nod.
  const { noduri, adancime } = useMemo(() => {
    const radacina: NodBuget = { cheie: '', denumire: '', nivel: -1, copii: [], articole: [], buget: 0, bugetMaterial: 0, consumat: 0 }
    if (!data) return { noduri: radacina, adancime: 0 }
    const t = cauta.trim().toLowerCase()
    const pastreaza = (r: RandBuget) =>
      (!t || r.simbol.toLowerCase().includes(t) || r.denumire.toLowerCase().includes(t))
      && (filtru === 'toate' || (filtru === 'cu-costuri' ? r.realizat.total > 0 : r.realizat.material > r.buget.material + 0.005))
    const dupaCheie = new Map<string, NodBuget>()
    let adancime = 0
    for (const r of data.articole.filter(pastreaza)) {
      const cale = !r.inDeviz ? ['Costuri fără articol în deviz'] : r.cale?.length ? r.cale : [r.capitol || '—']
      adancime = Math.max(adancime, cale.length)
      let nod = radacina
      for (let i = 0; i < cale.length; i++) {
        const cheie = cale.slice(0, i + 1).join(SEPARATOR)
        let copil = dupaCheie.get(cheie)
        if (!copil) {
          copil = { cheie, denumire: cale[i], nivel: i, copii: [], articole: [], buget: 0, bugetMaterial: 0, consumat: 0 }
          dupaCheie.set(cheie, copil)
          nod.copii.push(copil)
        }
        copil.buget += r.buget.total
        copil.bugetMaterial += r.buget.material
        copil.consumat += r.realizat.total
        nod = copil
      }
      nod.articole.push(r)
    }
    return { noduri: radacina, adancime }
  }, [data, cauta, filtru])

  const toateCheile = useMemo(() => {
    const chei: { cheie: string; nivel: number }[] = []
    const parcurge = (n: NodBuget) => n.copii.forEach((c) => { chei.push({ cheie: c.cheie, nivel: c.nivel }); parcurge(c) })
    parcurge(noduri)
    return chei
  }, [noduri])

  // Lista plată afișată: capitolele deschise cu ce e sub ele (rândurile articolelor se desfac în documente).
  const cuCautare = !!cauta.trim()
  const arbore = useMemo(() => {
    const randuri: RandTabel[] = []
    const parcurge = (n: NodBuget) => {
      for (const c of n.copii) {
        randuri.push({ cheie: `c-${c.cheie}`, capitol: c.denumire, nivel: c.nivel, areCopii: c.copii.length + c.articole.length > 0,
          buget: c.buget, bugetMaterial: c.bugetMaterial, consumat: c.consumat })
        if (cuCautare || !inchise.has(c.cheie)) parcurge(c)
      }
      for (const r of n.articole)
        randuri.push({ cheie: r.cheie ?? 'generale', rand: r, buget: r.buget.total, bugetMaterial: r.buget.material, consumat: r.realizat.total })
    }
    parcurge(noduri)
    return randuri
  }, [noduri, inchise, cuCautare])

  const comuta = (cheie: string) => setInchise((x) => {
    const n = new Set(x)
    if (n.has(cheie)) n.delete(cheie)
    else n.add(cheie)
    return n
  })
  /** Deschide arborele până la nivelul dat (1 = doar primul nivel); 0 = tot. */
  const laNivel = (nivel: number) => setInchise(new Set(nivel === 0 ? [] : toateCheile.filter((c) => c.nivel >= nivel - 1).map((c) => c.cheie)))

  if (isLoading) return <Skeleton active />
  if (error) return <Alert type="error" showIcon title={(error as Error).message} />
  if (!data) return null
  const r = data.rezumat
  const v = data.versiune

  const coloane: TableColumnsType<RandTabel> = [
    {
      title: 'Simbol', width: 150, fixed: 'left',
      render: (_, x) => {
        if (!x.capitol) return <Typography.Text code>{x.rand!.simbol}</Typography.Text>
        const cheie = x.cheie.slice(2)
        const inchis = !cuCautare && inchise.has(cheie)
        return (
          <span style={{ display: 'flex', alignItems: 'flex-start', gap: 4, paddingInlineStart: (x.nivel ?? 0) * 18 }}>
            <Button type="text" size="small" style={{ width: 20, height: 20, minWidth: 20, padding: 0, flex: 'none' }} disabled={!x.areCopii || cuCautare}
              icon={inchis ? <PlusSquareOutlined /> : <MinusSquareOutlined />} onClick={() => comuta(cheie)}
              aria-label={inchis ? 'Deschide capitolul' : 'Închide capitolul'} />
            <Typography.Text strong style={{ cursor: x.areCopii ? 'pointer' : undefined }} onClick={() => x.areCopii && comuta(cheie)}>{x.capitol}</Typography.Text>
          </span>
        )
      },
      onCell: (x) => (x.capitol ? { colSpan: 2 } : {}),
    },
    { title: 'Denumire', render: (_, x) => x.rand?.denumire, onCell: (x) => (x.capitol ? { colSpan: 0 } : {}) },
    { title: 'UM', width: 55, render: (_, x) => x.rand?.unitateMasura },
    { title: 'Cantitate', width: 95, className: 'numeric', render: (_, x) => (x.rand?.inDeviz ? formatCantitate(x.rand.cantitate) : '') },
    {
      title: <Tooltip title="Costul direct al articolului din devizul de referință (toate resursele)">Buget</Tooltip>,
      width: 125, className: 'numeric', render: (_, x) => <Typography.Text strong={!!x.capitol}>{x.buget ? formatLei(x.buget) : ''}</Typography.Text>,
    },
    {
      title: 'Buget material', width: 125, className: 'numeric',
      render: (_, x) => (x.bugetMaterial ? formatLei(x.bugetMaterial) : ''),
    },
    {
      title: <Tooltip title="Materialele consumate pe articol (bonuri de consum, la CMP)">Material consumat</Tooltip>,
      width: 135, className: 'numeric',
      render: (_, x) => <Typography.Text strong={!!x.capitol}>{x.consumat ? formatLei(x.consumat) : ''}</Typography.Text>,
    },
    {
      title: 'Consumat din bugetul de material', width: 150,
      render: (_, x) => <Consum buget={x.bugetMaterial} consumat={x.consumat} />,
    },
    {
      title: <Tooltip title="Buget material − consumat (negativ = depășire)">Rămas material</Tooltip>,
      width: 125, className: 'numeric',
      render: (_, x) => {
        if (!x.bugetMaterial && !x.consumat) return ''
        const d = x.bugetMaterial - x.consumat
        return <Typography.Text type={d < 0 ? 'danger' : undefined}>{formatLei(d)}</Typography.Text>
      },
    },
    {
      title: 'Manoperă / utilaj / transport (buget)', width: 170, className: 'numeric',
      render: (_, x) => x.rand && x.rand.inDeviz
        ? <Typography.Text type="secondary">{[x.rand.buget.manopera, x.rand.buget.utilaj, x.rand.buget.transport].map((n) => (n ? formatLei(n) : '—')).join(' / ')}</Typography.Text>
        : '',
    },
  ]

  const obiective = data.obiective ?? []
  const referinte = data.referinte ?? []
  const devize = data.devize ?? []
  return (
    <Space orientation="vertical" size={12} style={{ width: "100%" }}>
      {devize.length > 0 && (
        <Space wrap>
          <Typography.Text strong>Devizul:</Typography.Text>
          <Select value={alegere} onChange={setAlegere} style={{ minWidth: 420, maxWidth: '100%' }} popupMatchSelectWidth={false}
            showSearch={{ optionFilterProp: 'label' }}
            options={[
              { value: '', label: obiective.length > 1 ? 'Tot proiectul (devizele de referință ale tuturor obiectivelor)' : 'Devizul de referință al proiectului' },
              ...obiective.map((o) => ({
                label: `Obiectivul ${o.denumire}`,
                title: o.denumire,
                options: [
                  ...(obiective.length > 1 ? [{ value: `o:${o.id}`, label: `${o.denumire} — devizul de referință` }] : []),
                  ...devize.filter((d) => d.obiectivId === o.id).map((d) => ({
                    value: `v:${d.id}`,
                    label: `${o.denumire} · ${tipuriVersiune[d.tip as keyof typeof tipuriVersiune] ?? d.tip} nr. ${d.numar} · ${d.denumire} (${stariVersiune[d.stare].eticheta.toLowerCase()})`,
                  })),
                ],
              })),
            ]} />
          {alegere && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Pe un obiectiv sau deviz intră doar costurile pe articolele lui; cheltuielile generale, serviciile și materialele cumpărate sunt ale proiectului.
            </Typography.Text>
          )}
        </Space>
      )}
      {referinte.length > 1 ? (
        <Alert type={referinte.every((x) => x.versiune.stare === 'Aprobata') ? 'info' : 'warning'} showIcon
          title={<>Buget după devizele obiectivelor:{' '}
            {referinte.map((x, i) => (
              <span key={x.obiectivId}>{i > 0 && ' · '}<b>{x.obiectiv}</b>: <Link to={`/proiecte/${proiectId}/devize/${x.versiune.id}`}>
                {tipuriVersiune[x.versiune.tip as keyof typeof tipuriVersiune] ?? x.versiune.tip} nr. {x.versiune.numar}</Link>{' '}
                <Tag color={stariVersiune[x.versiune.stare].culoare}>{stariVersiune[x.versiune.stare].eticheta}</Tag></span>
            ))}</>}
          description="Costurile pe articole sunt deocamdată materialele consumate (bonuri de consum); serviciile facturate apar separat, ca nerepartizate." />
      ) : v ? (
        <Alert type={v.stare === 'Aprobata' ? 'info' : 'warning'} showIcon
          title={<>Buget după <Link to={`/proiecte/${proiectId}/devize/${v.id}`}>{tipuriVersiune[v.tip as keyof typeof tipuriVersiune] ?? v.tip} nr. {v.numar} · {v.denumire}</Link>{' '}
            <Tag color={stariVersiune[v.stare].culoare}>{stariVersiune[v.stare].eticheta}</Tag>
            {v.stare !== 'Aprobata' && 'Devizul nu e aprobat: bugetul urmează ciorna până la aprobare.'}</>}
          description="Costurile pe articole sunt deocamdată materialele consumate (bonuri de consum). Manopera (pontaj), utilajele și repartizarea facturilor de servicii pe articole vor veni ulterior; până atunci, serviciile facturate apar separat, ca nerepartizate." />
      ) : (
        <Alert type="warning" showIcon title="Proiectul nu are încă deviz: importă devizul (tab-ul Devize) ca să existe un buget pe articole." />
      )}

      <Row gutter={[12, 12]}>
        <Col xs={24} sm={12} xl={6}>
          <Card size="small">
            <Statistic title="Buget direct (deviz)" value={r.bugetDirect} formatter={(x) => formatLei(Number(x))} suffix="lei" />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>material {formatLei(r.bugetPeResurse.material)} · manoperă {formatLei(r.bugetPeResurse.manopera)}</Typography.Text>
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card size="small">
            <Statistic title="Costuri înregistrate" value={r.costuriInregistrate} formatter={(x) => formatLei(Number(x))} suffix="lei" />
            {r.procentCosturi !== null && <Progress percent={Math.min(r.procentCosturi, 100)} size="small" format={() => `${r.procentCosturi}% din buget`} status={r.procentCosturi > 100 ? 'exception' : 'normal'} />}
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card size="small" hoverable onClick={() => setDocumente({ titlu: 'Servicii facturate, nerepartizate pe articole', articol: 'servicii' })}>
            <Statistic title="Servicii facturate (nerepartizate)" value={r.serviciiNerepartizate} formatter={(x) => formatLei(Number(x))} suffix="lei" />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>cheltuieli generale (consum fără articol): {formatLei(r.cheltuieliGenerale)}</Typography.Text>
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card size="small" hoverable onClick={() => setDocumente({ titlu: 'Materiale cumpărate pe proiect (facturi cu linii de stoc)', articol: 'materiale' })}>
            <Statistic title="Materiale cumpărate" value={r.materialeCumparate} formatter={(x) => formatLei(Number(x))} suffix="lei" />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              consumate pe articole {formatLei(r.consumPeArticole)} · <Tooltip title="Cumpărate − consumate: în depozit sau pe șantier (aproximativ: consumul e la CMP, achiziția la prețul facturii)">neconsumate ≈ {formatLei(r.materialeNeconsumate)}</Tooltip>
            </Typography.Text>
          </Card>
        </Col>
      </Row>

      {data.articole.length > 0 && (
        <Card size="small" styles={{ body: { padding: 0 } }}
          title={
            <Space wrap>
              {adancime > 1 && (
                <Space.Compact size="small">
                  {Array.from({ length: Math.min(adancime, 4) }, (_, i) => (
                    <Tooltip key={i} title={`Deschide arborele până la nivelul ${i + 1}`}><Button size="small" onClick={() => laNivel(i + 1)}>{i + 1}</Button></Tooltip>
                  ))}
                  <Tooltip title="Deschide tot arborele, până la articole"><Button size="small" onClick={() => laNivel(0)}>Tot</Button></Tooltip>
                </Space.Compact>
              )}
              <Input.Search allowClear size="small" placeholder="Caută simbol sau denumire" style={{ width: 240 }} onChange={(e) => setCauta(e.target.value)} />
              <Segmented size="small" value={filtru} onChange={setFiltru}
                options={[{ value: 'toate', label: 'Toate' }, { value: 'cu-costuri', label: 'Cu costuri' }, { value: 'depasite', label: 'Depășite' }]} />
            </Space>
          }
          extra={<Typography.Text type="secondary">material consumat {formatLei(r.realizatPeResurse.material)} din {formatLei(r.bugetPeResurse.material)} ({r.bugetPeResurse.material > 0 ? formatProcent(r.realizatPeResurse.material / r.bugetPeResurse.material) : '—'})</Typography.Text>}>
          <Table<RandTabel> className="tabel-compact tabel-text-complet" rowKey="cheie" size="small" dataSource={arbore} columns={coloane} pagination={false} sticky
            scroll={{ x: 1450 }}
            expandable={{
              expandedRowRender: (x) => <DocumenteCost proiectId={proiectId} articol={x.rand?.cheie ?? 'generale'} />,
              rowExpandable: (x) => !!x.rand && x.rand.documente > 0,
            }}
            rowClassName={(x) => (x.capitol ? (x.nivel === 0 ? 'rand-capitol rand-capitol-0' : 'rand-capitol') : '')}
            locale={{ emptyText: <Empty description="Niciun articol pentru filtrul ales" /> }} />
        </Card>
      )}

      <Modal open={!!documente} title={documente?.titlu} footer={null} width={1000} onCancel={() => setDocumente(undefined)} destroyOnHidden>
        {documente && <DocumenteCost proiectId={proiectId} articol={documente.articol} />}
      </Modal>
    </Space>
  )
}
