import { Alert, Card, Col, Empty, Input, Modal, Progress, Row, Segmented, Skeleton, Space, Statistic, Table, Tag, Tooltip, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useBugetProiect, useCosturiBuget } from '../../api/buget'
import type { CostDocument, RandBuget } from '../../api/buget'
import { formatCantitate, formatData, formatLei, formatProcent, stariVersiune, tipuriVersiune } from '../../util/format'

/** Rând al tabelului: un capitol (cu sumele articolelor lui) sau un articol. */
interface RandTabel {
  cheie: string
  capitol?: string
  rand?: RandBuget
  buget: number
  bugetMaterial: number
  consumat: number
}

const naturi: Record<CostDocument['natura'], string> = { Consum: 'consum', AchizitieMateriale: 'materiale cumpărate', Serviciu: 'serviciu' }

/** Documentele cu costuri ale unui rând (articol, cheltuieli generale, servicii, materiale cumpărate). */
function DocumenteCost({ proiectId, articol }: { proiectId: string; articol: string }) {
  const { data, isLoading } = useCosturiBuget(proiectId, articol)
  return (
    <Table<CostDocument> className="tabel-compact" size="small" loading={isLoading} dataSource={data} pagination={data && data.length > 20 ? { pageSize: 20, size: 'small' } : false}
      rowKey={(c, i) => `${c.documentId}-${i}`} locale={{ emptyText: 'Niciun document' }}
      columns={[
        { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
        { title: 'Document', dataIndex: 'document', width: 260, ellipsis: true },
        { title: 'Natura', dataIndex: 'natura', width: 140, render: (n: CostDocument['natura']) => naturi[n] },
        { title: 'Detalii', dataIndex: 'descriere', ellipsis: true },
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
  const { data, isLoading, error } = useBugetProiect(proiectId)
  const { token } = theme.useToken()
  const [cauta, setCauta] = useState('')
  const [filtru, setFiltru] = useState<'toate' | 'cu-costuri' | 'depasite'>('toate')
  const [documente, setDocumente] = useState<{ titlu: string; articol: string }>()

  const arbore = useMemo(() => {
    if (!data) return []
    const t = cauta.trim().toLowerCase()
    const pastreaza = (r: RandBuget) =>
      (!t || r.simbol.toLowerCase().includes(t) || r.denumire.toLowerCase().includes(t))
      && (filtru === 'toate' || (filtru === 'cu-costuri' ? r.realizat.total > 0 : r.realizat.material > r.buget.material + 0.005))
    const grupuri = new Map<string, RandBuget[]>()
    for (const r of data.articole.filter(pastreaza)) {
      const g = r.inDeviz ? r.capitol : 'Costuri fără articol în deviz'
      grupuri.set(g, [...(grupuri.get(g) ?? []), r])
    }
    // Listă plată: rândul capitolului (cu sumele lui), apoi articolele lui (rândurile articolelor se desfac în documente).
    return [...grupuri.entries()].flatMap(([capitol, randuri]): RandTabel[] => [
      {
        cheie: `c-${capitol}`,
        capitol,
        buget: randuri.reduce((s, r) => s + r.buget.total, 0),
        bugetMaterial: randuri.reduce((s, r) => s + r.buget.material, 0),
        consumat: randuri.reduce((s, r) => s + r.realizat.total, 0),
      },
      ...randuri.map((r) => ({ cheie: r.cheie ?? 'generale', rand: r, buget: r.buget.total, bugetMaterial: r.buget.material, consumat: r.realizat.total })),
    ])
  }, [data, cauta, filtru])

  if (isLoading) return <Skeleton active />
  if (error) return <Alert type="error" showIcon title={(error as Error).message} />
  if (!data) return null
  const r = data.rezumat
  const v = data.versiune

  const coloane: TableColumnsType<RandTabel> = [
    {
      title: 'Simbol', width: 150, fixed: 'left',
      render: (_, x) => x.capitol ? <Typography.Text strong>{x.capitol}</Typography.Text> : <Typography.Text code>{x.rand!.simbol}</Typography.Text>,
      onCell: (x) => (x.capitol ? { colSpan: 2 } : {}),
    },
    { title: 'Denumire', ellipsis: true, render: (_, x) => x.rand?.denumire, onCell: (x) => (x.capitol ? { colSpan: 0 } : {}) },
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
      title: 'Consumat din bugetul de material', width: 190,
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
      title: 'Manoperă / utilaj / transport (buget)', width: 200, className: 'numeric',
      render: (_, x) => x.rand && x.rand.inDeviz
        ? <Typography.Text type="secondary">{[x.rand.buget.manopera, x.rand.buget.utilaj, x.rand.buget.transport].map((n) => (n ? formatLei(n) : '—')).join(' / ')}</Typography.Text>
        : '',
    },
  ]

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      {v ? (
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
              <Input.Search allowClear size="small" placeholder="Caută simbol sau denumire" style={{ width: 240 }} onChange={(e) => setCauta(e.target.value)} />
              <Segmented size="small" value={filtru} onChange={setFiltru}
                options={[{ value: 'toate', label: 'Toate' }, { value: 'cu-costuri', label: 'Cu costuri' }, { value: 'depasite', label: 'Depășite' }]} />
            </Space>
          }
          extra={<Typography.Text type="secondary">material consumat {formatLei(r.realizatPeResurse.material)} din {formatLei(r.bugetPeResurse.material)} ({r.bugetPeResurse.material > 0 ? formatProcent(r.realizatPeResurse.material / r.bugetPeResurse.material) : '—'})</Typography.Text>}>
          <Table<RandTabel> className="tabel-compact" rowKey="cheie" size="small" dataSource={arbore} columns={coloane} pagination={false} sticky
            scroll={{ x: 1350 }}
            expandable={{
              expandedRowRender: (x) => <DocumenteCost proiectId={proiectId} articol={x.rand?.cheie ?? 'generale'} />,
              rowExpandable: (x) => !!x.rand && x.rand.documente > 0,
            }}
            onRow={(x) => ({ style: x.capitol ? { background: token.colorFillSecondary } : undefined })}
            locale={{ emptyText: <Empty description="Niciun articol pentru filtrul ales" /> }} />
        </Card>
      )}

      <Modal open={!!documente} title={documente?.titlu} footer={null} width={1000} onCancel={() => setDocumente(undefined)} destroyOnHidden>
        {documente && <DocumenteCost proiectId={proiectId} articol={documente.articol} />}
      </Modal>
    </Space>
  )
}
