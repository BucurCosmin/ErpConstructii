import { ShoppingCartOutlined } from '@ant-design/icons'
import { Alert, App, Button, Collapse, Flex, Modal, Segmented, Space, Statistic, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useAplicaPreturi, useDiferentePreturi } from '../../api/devize'
import type { PoliticaPret, PropunerePret, StarePropunere } from '../../api/devize'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatData, formatLei, formatProcent } from '../../util/format'

const pret = new Intl.NumberFormat('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 4 })
const formatPret = (v: number | null | undefined) => (v === null || v === undefined ? '—' : pret.format(v))

const tipuri: Record<PropunerePret['tip'], string> = { Material: 'material', Manopera: 'manoperă', Utilaj: 'utilaj', Transport: 'transport', Echipament: 'echipament' }

const stari: Record<StarePropunere, { eticheta: string; culoare: string; explicatie: string }> = {
  Diferit: { eticheta: 'diferit', culoare: 'orange', explicatie: 'Prețul de achiziție diferă de cel din deviz.' },
  Egal: { eticheta: 'la fel', culoare: 'green', explicatie: 'Prețul de achiziție e același cu cel din deviz.' },
  FaraAchizitiiRecente: { eticheta: 'fără achiziții recente', culoare: 'default', explicatie: 'Materialul are achiziții, dar nu în ultimele 6 luni: alege „Ultimul preț”.' },
  FaraAchizitii: { eticheta: 'fără achiziții', culoare: 'default', explicatie: 'Materialul din stoc legat de resursă nu a fost încă recepționat cu preț.' },
  Nelegata: { eticheta: 'nelegată', culoare: 'red', explicatie: 'Resursa nu e legată de un material din stoc (Ofertare și devize → Nomenclator resurse).' },
}

/**
 * Prețurile resurselor din deviz față de prețurile de achiziție (recepții = prețul facturilor), D-55 etapa 3: diferența și
 * impactul pe costul direct, apoi aplicarea prețurilor alese în ciorna de lucru (devizul importat / aprobat rămâne neschimbat).
 */
export function PreturiAchizitie({ proiectId, versiuneId, poateAplica }: { proiectId: string; versiuneId: string; poateAplica: boolean }) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const [deschis, setDeschis] = useState(false)
  const [politica, setPolitica] = useState<PoliticaPret>('MedieInterval')
  const [filtru, setFiltru] = useState<'diferite' | 'legate' | 'toate'>('diferite')
  const { data, isFetching, error } = useDiferentePreturi(proiectId, versiuneId, politica, deschis)
  const aplica = useAplicaPreturi(proiectId)
  const [alese, setAlese] = useState<React.Key[]>([])
  /** Prețul ales de utilizator în locul celui propus (cod → preț). */
  const [modificate, setModificate] = useState<Record<string, number>>({})

  // La fiecare raport nou, sunt alese toate resursele cu prețuri diferite.
  useEffect(() => {
    setAlese(data?.resurse.filter((r) => r.stare === 'Diferit').map((r) => r.cod) ?? [])
    setModificate({})
  }, [data])

  const pretAles = (r: PropunerePret) => modificate[r.cod] ?? r.pretPropus
  const impactAles = (r: PropunerePret) => {
    const p = pretAles(r)
    if (p === null || p === undefined) return 0
    return r.pretPropus !== null && p === r.pretPropus ? r.impact : r.cantitateDeviz * (p - r.pretDeviz)
  }
  const impactSelectat = (data?.resurse ?? []).filter((r) => alese.includes(r.cod)).reduce((s, r) => s + impactAles(r), 0)
  const randuri = (data?.resurse ?? []).filter((r) =>
    filtru === 'toate' || (filtru === 'legate' ? r.stare !== 'Nelegata' : r.stare === 'Diferit'))
  const numar = (s: StarePropunere) => data?.resurse.filter((r) => r.stare === s).length ?? 0

  const aplicaPreturi = async () => {
    const preturi = (data?.resurse ?? []).filter((r) => alese.includes(r.cod) && pretAles(r) !== null).map((r) => ({ cod: r.cod, pret: pretAles(r)! }))
    try {
      const r = await aplica.mutateAsync({ versiuneId, preturi })
      message.success({
        content: `Prețurile a ${preturi.length} resurse au fost aplicate pe ${r.articoleModificate} articole, în ${r.versiuneNoua ? 'ciorna de lucru nouă' : 'ciorna de lucru'} nr. ${r.numar}. Modificările apar marcate; „Aprobă versiunea” le confirmă.`,
        duration: 8,
      })
      setDeschis(false)
      if (r.versiuneId !== versiuneId) navigheaza(`/proiecte/${proiectId}/devize/${r.versiuneId}`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const coloane: TableColumnsType<PropunerePret> = [
    { title: 'Cod', dataIndex: 'cod', width: 100, render: (v) => <Typography.Text code>{v}</Typography.Text> },
    {
      title: 'Resursa', dataIndex: 'denumire', ellipsis: { showTitle: true },
      render: (v, r) => <>{v} <Typography.Text type="secondary" style={{ fontSize: 11 }}>{tipuri[r.tip]} · {r.articole} art.</Typography.Text></>,
    },
    { title: 'UM', dataIndex: 'unitateMasura', width: 55 },
    { title: 'Cantitate în deviz', dataIndex: 'cantitateDeviz', width: 115, className: 'numeric', render: formatCantitate },
    {
      title: 'Preț deviz', dataIndex: 'pretDeviz', width: 105, className: 'numeric',
      render: (v: number, r) => r.preturiDiferiteInDeviz
        ? <Tooltip title="Resursa are prețuri diferite în articole: media ponderată">{formatPret(v)}*</Tooltip>
        : formatPret(v),
    },
    {
      title: 'Ultima achiziție', width: 150, className: 'numeric',
      render: (_, r) => r.achizitii
        ? <Tooltip title={`${r.achizitii.documentUltim}${r.achizitii.furnizorUltim ? ` · ${r.achizitii.furnizorUltim}` : ''}`}>
            {formatPret(r.achizitii.ultimul)} <Typography.Text type="secondary" style={{ fontSize: 11 }}>{formatData(r.achizitii.dataUltimului)}</Typography.Text>
          </Tooltip>
        : '—',
    },
    {
      title: <Tooltip title="Media ponderată cu cantitățile a recepțiilor din ultimele 6 luni (și câte recepții)">Medie 6 luni</Tooltip>, width: 120, className: 'numeric',
      render: (_, r) => r.achizitii?.medieInterval
        ? <>{formatPret(r.achizitii.medieInterval)} <Typography.Text type="secondary" style={{ fontSize: 11 }}>({r.achizitii.achizitiiInInterval})</Typography.Text></>
        : '—',
    },
    {
      title: 'Preț nou', width: 130, className: 'numeric',
      render: (_, r) => r.pretPropus === null && !modificate[r.cod]
        ? <Tooltip title={stari[r.stare].explicatie}><Tag color={stari[r.stare].culoare}>{stari[r.stare].eticheta}</Tag></Tooltip>
        : poateAplica && alese.includes(r.cod)
          ? <NumarRo size="small" min={0} style={{ width: 110 }} value={pretAles(r) ?? undefined}
              onChange={(v) => setModificate((m) => ({ ...m, [r.cod]: typeof v === 'number' ? v : (r.pretPropus ?? 0) }))} />
          : <b>{formatPret(pretAles(r))}</b>,
    },
    {
      title: 'Diferență', dataIndex: 'diferenta', width: 90, className: 'numeric',
      render: (_, r) => {
        const p = pretAles(r)
        if (p === null || p === undefined || r.pretDeviz <= 0) return ''
        const d = (p - r.pretDeviz) / r.pretDeviz
        return <Typography.Text type={d > 0.005 ? 'danger' : d < -0.005 ? 'success' : 'secondary'}>{d > 0 ? '+' : ''}{formatProcent(d)}</Typography.Text>
      },
    },
    {
      title: <Tooltip title="Cu cât se schimbă costul direct al devizului (cantitatea din deviz × diferența de preț)">Impact (lei)</Tooltip>,
      width: 120, className: 'numeric',
      render: (_, r) => {
        const i = impactAles(r)
        return i ? <Typography.Text type={i > 0 ? 'danger' : 'success'}>{i > 0 ? '+' : ''}{formatLei(i)}</Typography.Text> : ''
      },
    },
  ]

  return (
    <>
      <Tooltip title="Prețurile resurselor din deviz față de prețurile de achiziție din facturi (recepții); prețurile alese se aplică în ciorna de lucru.">
        <Button icon={<ShoppingCartOutlined />} onClick={() => setDeschis(true)}>Prețuri de achiziție</Button>
      </Tooltip>
      <Modal open={deschis} title="Prețurile din deviz față de prețurile de achiziție" width={1250} onCancel={() => setDeschis(false)} destroyOnHidden
        footer={
          <Flex justify="space-between" align="center">
            <Typography.Text type="secondary">Se aplică în ciorna de lucru: devizul importat / aprobat rămâne neschimbat până aprobi ciorna.</Typography.Text>
            <Space>
              <Button onClick={() => setDeschis(false)}>Închide</Button>
              {poateAplica && (
                <Button type="primary" disabled={alese.length === 0} loading={aplica.isPending} onClick={() => void aplicaPreturi()}>
                  Aplică {alese.length} prețuri
                </Button>
              )}
            </Space>
          </Flex>
        }>
        {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 12 }} />}
        <Flex wrap gap={24} align="center" style={{ marginBottom: 12 }}>
          <Segmented<PoliticaPret> value={politica} onChange={setPolitica}
            options={[
              { value: 'MedieInterval', label: 'Medie 6 luni' },
              { value: 'Ultimul', label: 'Ultimul preț' },
              { value: 'MinimInterval', label: 'Cel mai mic (6 luni)' },
            ]} />
          {data && (
            <>
              <Statistic title="Cost direct actual" value={data.costDirectActual} formatter={(v) => formatLei(Number(v))} valueStyle={{ fontSize: 18 }} />
              <Statistic title="Impact prețuri alese" value={impactSelectat} formatter={(v) => `${Number(v) > 0 ? '+' : ''}${formatLei(Number(v))}`}
                valueStyle={{ fontSize: 18, color: impactSelectat > 0 ? '#cf1322' : impactSelectat < 0 ? '#389e0d' : undefined }} />
              <Statistic title="Cost direct nou" value={data.costDirectActual + impactSelectat} formatter={(v) => formatLei(Number(v))} valueStyle={{ fontSize: 18 }} />
            </>
          )}
        </Flex>
        {data && numar('Nelegata') > 0 && (
          <Alert type="info" showIcon style={{ marginBottom: 12 }}
            title={<>{numar('Nelegata')} resurse nu sunt legate de materiale din stoc și nu au prețuri de achiziție: leagă-le în <Link to="/ofertare?tab=nomenclator">Ofertare și devize → Nomenclator resurse</Link>.</>} />
        )}
        {data && data.articoleModificateManual.length > 0 && (
          <Collapse size="small" style={{ marginBottom: 12 }}
            items={[{
              key: 'm',
              label: `${data.articoleModificateManual.length} articole au prețul modificat manual (nu mai e suma resurselor): primesc doar diferența adusă de resursele schimbate, restul prețului rămâne.`,
              children: <ul style={{ margin: 0 }}>{data.articoleModificateManual.map((a) => <li key={a}>{a}</li>)}</ul>,
            }]} />
        )}
        <Flex justify="space-between" style={{ marginBottom: 8 }}>
          <Segmented size="small" value={filtru} onChange={setFiltru}
            options={[
              { value: 'diferite', label: `Cu diferențe (${numar('Diferit')})` },
              { value: 'legate', label: 'Legate de stoc' },
              { value: 'toate', label: `Toate (${data?.resurse.length ?? 0})` },
            ]} />
          <Typography.Text type="secondary">Sortate după impact; prețul nou se poate corecta înainte de aplicare.</Typography.Text>
        </Flex>
        <Table<PropunerePret> className="tabel-compact" rowKey="cod" size="small" loading={isFetching} dataSource={randuri} columns={coloane}
          pagination={randuri.length > 50 ? { pageSize: 50, size: 'small' } : false} scroll={{ x: 1150, y: 480 }}
          rowSelection={poateAplica ? {
            selectedRowKeys: alese,
            onChange: setAlese,
            getCheckboxProps: (r) => ({ disabled: r.pretPropus === null && modificate[r.cod] === undefined }),
          } : undefined}
          locale={{ emptyText: filtru === 'diferite' ? 'Nicio resursă cu preț de achiziție diferit de cel din deviz' : 'Nicio resursă' }} />
      </Modal>
    </>
  )
}
