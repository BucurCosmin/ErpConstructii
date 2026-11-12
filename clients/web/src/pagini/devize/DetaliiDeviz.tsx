import { CheckOutlined, CopyOutlined, DeleteOutlined, DownloadOutlined, EditOutlined, PlusOutlined, PrinterOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Flex, Form, Input, Modal, Popconfirm, Progress, Row, Segmented, Skeleton, Space, Statistic, Switch, Table, Tag, Tooltip, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../../api/client'
import {
  useAdaugaArticolDeviz,
  useAprobaDeviz,
  useDiferenteDeviz,
  useModificaArticolDeviz,
  useRedenumesteDeviz,
  useStergeArticolDeviz,
  useStergeDeviz,
  useVersiuneNouaDeviz,
} from '../../api/devize'
import type { ArticolCerere, DiferentaVersiune, RezultatEditareDeviz } from '../../api/devize'
import { useDeviz, useDevize, usePermisiuni, useProiect } from '../../api/interogari'
import type { ArticolDeviz, CapitolDeviz, ValoriResurse } from '../../api/tipuri'
import { NumarRo } from '../../componente/NumarRo'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatCantitate, formatLei, formatProcent, stariVersiune, tipuriVersiune } from '../../util/format'
import { deschidePdf } from '../../util/pdf'

/** Rând din arborele devizului: capitole pe oricâte niveluri (obiect, stadiu fizic, capitol), apoi articole. */
interface Rand {
  cheie: string
  nivel: 'capitol' | 'articol'
  /** Adâncimea capitolului (0 = primul nivel), pentru nuanța rândului. */
  adancime: number
  cod: string
  denumire: string
  um?: string
  cantitate?: number
  pretUnitar?: ValoriResurse
  valoare?: ValoriResurse
  total: number
  ofertaUnitar?: number
  ofertaTotal?: number
  subcontractata?: boolean
  asimilat?: boolean
  /** Articolul (pentru editare) și capitolul lui; la rândurile de capitol, capitolul (pentru „adaugă articol”). */
  articol?: ArticolDeviz
  capitolId: string
  children?: Rand[]
}

interface ValoriArticol {
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  material?: number
  manopera?: number
  utilaj?: number
  transport?: number
  echipament?: number
}

function randArticol(a: ArticolDeviz, adancime: number, capitolId: string): Rand {
  return {
    cheie: a.id,
    nivel: 'articol',
    adancime,
    cod: a.simbol,
    denumire: a.denumire,
    um: a.unitateMasura,
    cantitate: a.cantitate,
    pretUnitar: a.pretUnitar,
    valoare: a.valoare,
    total: a.valoare.total,
    ofertaUnitar: a.pretOfertaUnitar?.total,
    ofertaTotal: a.valoareOferta?.total,
    asimilat: a.tipArticol === 'Asimilat',
    articol: a,
    capitolId,
  }
}

/** Potrivirea căutării: simbol, denumire sau cod WBS (fără diacritice, fără majuscule). */
const normalizeaza = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/**
 * Arborele, păstrând doar ce se potrivește cu căutarea (și capitolele care conțin potriviri); fără căutare, tot.
 * `pastreaza` (ex. „doar modificările”) restrânge în plus articolele; capitolele rămase goale dispar.
 */
function randCapitol(c: CapitolDeviz, adancime: number, cauta: string, pastreaza?: (a: ArticolDeviz) => boolean): Rand | null {
  const potriveste = (t?: string | null) => !!t && normalizeaza(t).includes(cauta)
  const capitolPotrivit = !cauta || potriveste(c.denumire) || potriveste(c.codWbs) || potriveste(c.cod)
  const articole = c.articole
    .filter((a) => capitolPotrivit || potriveste(a.simbol) || potriveste(a.denumire) || potriveste(a.codWbs))
    .filter((a) => !pastreaza || pastreaza(a))
    .map((a) => randArticol(a, adancime + 1, c.id))
  const sub = c.subcapitole.map((s) => randCapitol(s, adancime + 1, capitolPotrivit ? '' : cauta, pastreaza)).filter((x): x is Rand => x !== null)
  const copii = [...articole, ...sub]
  if (copii.length === 0 && ((cauta && !capitolPotrivit) || pastreaza)) return null
  return {
    cheie: c.id,
    nivel: 'capitol',
    adancime,
    cod: c.codWbs ?? c.cod,
    denumire: c.denumire,
    total: c.total,
    subcontractata: c.subcontractat,
    capitolId: c.id,
    children: copii.length ? copii : undefined,
  }
}

function capitoleToate(c: CapitolDeviz[]): CapitolDeviz[] {
  return c.flatMap((x) => [x, ...capitoleToate(x.subcapitole)])
}

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nume
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

type Vedere = 'valori' | 'preturi'

export function DetaliiDeviz() {
  const { proiectId = '', versiuneId = '' } = useParams()
  const navigheaza = useNavigate()
  const { message } = App.useApp()
  const proiect = useProiect(proiectId)
  const permisiuni = usePermisiuni()
  const { data, isLoading, error } = useDeviz(proiectId, versiuneId)
  const { token } = theme.useToken()
  const [vedere, setVedere] = useState<Vedere>('valori')
  const [cauta, setCauta] = useState('')
  const [deEditat, setDeEditat] = useState<{ capitolId: string; articol?: ArticolDeviz }>()
  const [redenumire, setRedenumire] = useState<string>()
  const [formArticol] = Form.useForm<ValoriArticol>()
  const [seExporta, setSeExporta] = useState(false)

  const redenumeste = useRedenumesteDeviz(proiectId)
  const sterge = useStergeDeviz(proiectId)
  const aproba = useAprobaDeviz(proiectId)
  const versiuneNoua = useVersiuneNouaDeviz(proiectId)
  const modificaArticol = useModificaArticolDeviz(proiectId)
  const adaugaArticol = useAdaugaArticolDeviz(proiectId)
  const stergeArticol = useStergeArticolDeviz(proiectId)

  const poateEdita = permisiuni.data?.includes('ofertare.editare') ?? false
  const poateAproba = permisiuni.data?.includes('ofertare.aprobare') ?? false
  const ciorna = data?.versiune.stare === 'Ciorna'
  /** Ciorna de lucru: pornită din altă versiune; doar ea se modifică pe loc (D-53). */
  const deLucru = ciorna && !!data?.versiune.versiuneAnterioaraId
  /** Pe devizul importat și pe cel aprobat, prima modificare creează ciorna de lucru; cel înlocuit rămâne doar în istoric. */
  const editabil = poateEdita && !!data && data.versiune.stare !== 'Inlocuita'
  const toate = useDevize(proiectId)
  const ciornaExistenta = toate.data?.find((v) => v.versiuneAnterioaraId === versiuneId && v.stare === 'Ciorna')
  const anterioara = toate.data?.find((v) => v.id === data?.versiune.versiuneAnterioaraId)
  const diferente = useDiferenteDeviz(proiectId, versiuneId, !!data?.versiune.versiuneAnterioaraId)
  const dupaCheie = useMemo(() => new Map((diferente.data?.diferente ?? []).map((d) => [d.cheieStabila, d])), [diferente.data])
  const [doarModificari, setDoarModificari] = useState(false)

  const textCautat = normalizeaza(cauta.trim())
  const arbore = useMemo(
    () => (data
      ? data.capitole
        .map((c) => randCapitol(c, 0, textCautat, doarModificari ? (a) => dupaCheie.has(a.cheieStabila) : undefined))
        .filter((x): x is Rand => x !== null)
      : []),
    [data, textCautat, doarModificari, dupaCheie],
  )
  const capitole = useMemo(() => (data ? capitoleToate(data.capitole) : []), [data])
  const chei = useMemo(() => capitole.map((c) => c.id), [capitole])
  const articole = useMemo(() => capitole.flatMap((c) => c.articole), [capitole])
  const gasite = useMemo(() => {
    const numara = (r: Rand[]): number => r.reduce((s, x) => s + (x.nivel === 'articol' ? 1 : numara(x.children ?? [])), 0)
    return numara(arbore)
  }, [arbore])
  const nrArticole = articole.length
  const areEchipament = (data?.recapitulatie.cheltuieliDirecteResurse.echipament ?? 0) !== 0
  const areOferta = articole.some((a) => a.pretOfertaUnitar)
  const moneda = data?.versiune.moneda ?? 'RON'
  const formatOferta = (v: number) => (moneda === 'RON' ? formatLei(v) : `${v.toLocaleString('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${moneda}`)

  const eroare = (e: unknown) => message.error((e as Error).message)

  const deschideArticol = (capitolId: string, articol?: ArticolDeviz) => {
    setDeEditat({ capitolId, articol })
    formArticol.setFieldsValue(articol
      ? {
          simbol: articol.simbol, denumire: articol.denumire, unitateMasura: articol.unitateMasura, cantitate: articol.cantitate,
          material: articol.pretUnitar.material, manopera: articol.pretUnitar.manopera, utilaj: articol.pretUnitar.utilaj,
          transport: articol.pretUnitar.transport, echipament: articol.pretUnitar.echipament,
        }
      : { simbol: '', denumire: '', unitateMasura: '', cantitate: undefined, material: 0, manopera: 0, utilaj: 0, transport: 0, echipament: 0 })
  }

  const salveazaArticol = async () => {
    if (!deEditat) return
    const v = await formArticol.validateFields()
    const date: ArticolCerere = {
      simbol: v.simbol.trim(), denumire: v.denumire.trim(), unitateMasura: v.unitateMasura.trim(), cantitate: v.cantitate,
      pretUnitar: { material: v.material ?? 0, manopera: v.manopera ?? 0, utilaj: v.utilaj ?? 0, transport: v.transport ?? 0, echipament: v.echipament ?? 0 },
    }
    try {
      const r = deEditat.articol
        ? await modificaArticol.mutateAsync({ versiuneId, articolId: deEditat.articol.id, date })
        : await adaugaArticol.mutateAsync({ versiuneId, capitolId: deEditat.capitolId, date })
      setDeEditat(undefined)
      dupaEditare(r, deEditat.articol ? 'Articolul a fost modificat.' : 'Articolul a fost adăugat.')
    } catch (e) {
      eroare(e)
    }
  }

  /** Modificarea pe devizul importat / aprobat s-a salvat în ciorna de lucru: o deschidem, cu modificările marcate. */
  const dupaEditare = (r: RezultatEditareDeviz, mesaj: string) => {
    if (r.versiuneId === versiuneId) {
      message.success(mesaj)
      return
    }
    message.success({
      content: `${mesaj} Modificarea e în ${r.versiuneNoua ? 'ciorna de lucru nouă' : 'ciorna de lucru'} nr. ${r.numar}; versiunea nr. ${data?.versiune.numar} a rămas neschimbată până la aprobare.`,
      duration: 8,
    })
    navigheaza(`/proiecte/${proiectId}/devize/${r.versiuneId}`)
  }

  const textDiferenta = (d: DiferentaVersiune) => {
    if (d.tip === 'Adaugat') return 'Articol adăugat în această versiune.'
    if (!d.vechi || !d.nou) return ''
    const v = d.vechi
    const n = d.nou
    const parti = [
      d.campuri.includes('simbol') && `simbol: ${v.simbol} → ${n.simbol}`,
      d.campuri.includes('denumire') && `denumire: ${v.denumire}`,
      d.campuri.includes('um') && `UM: ${v.unitateMasura} → ${n.unitateMasura}`,
      d.campuri.includes('cantitate') && `cantitate: ${formatCantitate(v.cantitate)} → ${formatCantitate(n.cantitate)}`,
      d.campuri.includes('pret') && `preț unitar: ${formatLei(v.pretUnitar)} → ${formatLei(n.pretUnitar)}`,
      `valoare: ${formatLei(v.valoare)} → ${formatLei(n.valoare)}`,
    ].filter(Boolean)
    return `Înainte (nr. ${diferente.data?.numarVechi}): ${parti.join('; ')}`
  }

  const exportExcel = async () => {
    setSeExporta(true)
    try {
      descarca(await api.getBinar(`/api/proiecte/${proiectId}/devize/${versiuneId}/excel`), `Deviz_${data?.versiune.numar ?? ''}.xlsx`)
    } catch (e) {
      eroare(e)
    } finally {
      setSeExporta(false)
    }
  }

  const resursa = (titlu: string, k: keyof Omit<ValoriResurse, 'total'>) => ({
    title: titlu,
    width: 120,
    className: 'numeric',
    render: (_: unknown, r: Rand) => {
      const v = vedere === 'valori' ? r.valoare : r.pretUnitar
      return v && v[k] !== 0 ? formatLei(v[k]) : ''
    },
  })

  const coloane: TableColumnsType<Rand> = [
    {
      title: 'Cod / simbol',
      dataIndex: 'cod',
      width: 190,
      fixed: 'left',
      render: (v, r) => (r.nivel === 'articol' ? <Typography.Text code>{v}</Typography.Text> : <Typography.Text strong>{v}</Typography.Text>),
    },
    {
      title: 'Denumire',
      dataIndex: 'denumire',
      render: (v, r) => (
        <>
          <Typography.Text strong={r.nivel !== 'articol'}>{v}</Typography.Text>
          {r.subcontractata && <Tag color="purple" style={{ marginInlineStart: 8 }}>subcontractat</Tag>}
          {r.asimilat && <Tag style={{ marginInlineStart: 8 }}>asimilat</Tag>}
          {r.articol && dupaCheie.get(r.articol.cheieStabila) && (() => {
            const d = dupaCheie.get(r.articol!.cheieStabila)!
            return (
              <Tooltip title={textDiferenta(d)}>
                <Tag color={d.tip === 'Adaugat' ? 'green' : 'gold'} style={{ marginInlineStart: 8, cursor: 'help' }}>
                  {d.tip === 'Adaugat' ? 'nou' : 'modificat'}
                </Tag>
              </Tooltip>
            )
          })()}
        </>
      ),
    },
    { title: 'UM', dataIndex: 'um', width: 70 },
    { title: 'Cantitate', dataIndex: 'cantitate', width: 110, className: 'numeric', render: (v?: number) => (v === undefined ? '' : formatCantitate(v)) },
    resursa('Material', 'material'),
    resursa('Manoperă', 'manopera'),
    resursa('Utilaj', 'utilaj'),
    resursa('Transport', 'transport'),
    ...(areEchipament ? [resursa('Echipament', 'echipament')] : []),
    ...(areOferta ? [{
      title: vedere === 'valori' ? `Valoare ofertă (${moneda})` : `Preț ofertă (${moneda})`,
      width: 150,
      className: 'numeric',
      render: (_: unknown, r: Rand) => {
        const v = vedere === 'valori' ? r.ofertaTotal : r.ofertaUnitar
        return v === undefined ? '' : formatOferta(v)
      },
    }] : []),
    {
      title: vedere === 'valori' ? 'Valoare' : 'Preț unitar',
      width: 140,
      fixed: 'right',
      className: 'numeric',
      render: (_, r) => {
        const v = vedere === 'valori' ? r.total : r.pretUnitar?.total
        return v === undefined ? '' : <Typography.Text strong={r.nivel !== 'articol'}>{formatLei(v)}</Typography.Text>
      },
    },
    ...(editabil ? [{
      title: '',
      key: 'actiuni',
      width: 84,
      fixed: 'right' as const,
      render: (_: unknown, r: Rand) => r.nivel === 'articol' && r.articol
        ? (
          <Space size={0}>
            <Tooltip title="Modifică articolul"><Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschideArticol(r.capitolId, r.articol)} /></Tooltip>
            <Popconfirm title={`Scoți articolul ${r.cod} din deviz?`} okText="Scoate" okButtonProps={{ danger: true }} cancelText="Nu"
              onConfirm={() => stergeArticol.mutateAsync({ versiuneId, articolId: r.articol!.id }).then((x) => dupaEditare(x, 'Articolul a fost scos.'), eroare)}>
              <Button size="small" type="text" danger icon={<DeleteOutlined />} />
            </Popconfirm>
          </Space>
        )
        : <Tooltip title="Adaugă un articol în capitol"><Button size="small" type="text" icon={<PlusOutlined />} onClick={() => deschideArticol(r.capitolId)} /></Tooltip>,
    }] : []),
  ]

  const rec = data?.recapitulatie
  const directe = rec?.cheltuieliDirecteResurse

  return (
    <>
      <AntetPagina
        cale={[
          { titlu: 'Acasă', la: '/' },
          { titlu: 'Proiecte', la: '/proiecte' },
          { titlu: proiect.data?.cod ?? '…', la: `/proiecte/${proiectId}?tab=devize` },
          { titlu: data ? `${tipuriVersiune[data.versiune.tip]} nr. ${data.versiune.numar}` : '…' },
        ]}
        titlu={
          data ? (
            <>
              {data.versiune.denumire}{' '}
              <Tag color={stariVersiune[data.versiune.stare].culoare} style={{ verticalAlign: 'middle' }}>
                {stariVersiune[data.versiune.stare].eticheta}
              </Tag>
            </>
          ) : (
            <Skeleton.Input active />
          )
        }
        subtitlu={proiect.data ? `${proiect.data.cod} · ${proiect.data.denumire}` : undefined}
        actiuni={
          <>
            <Button icon={<PrinterOutlined />} disabled={!data}
              onClick={() => void deschidePdf(`/api/proiecte/${proiectId}/devize/${versiuneId}/pdf`).catch(eroare)}>
              Tipărește
            </Button>
            <Button icon={<DownloadOutlined />} disabled={!data} loading={seExporta} onClick={() => void exportExcel()}>
              Export Excel
            </Button>
            {ciorna && poateEdita && (
              <>
                <Button icon={<EditOutlined />} onClick={() => setRedenumire(data?.versiune.denumire ?? '')}>Redenumește</Button>
                <Popconfirm title="Ștergi această ciornă de deviz?" description="Se șterg și fișierele ei originale." okText="Șterge"
                  okButtonProps={{ danger: true }} cancelText="Nu"
                  onConfirm={() => sterge.mutateAsync(versiuneId).then(() => { message.success('Ciorna a fost ștearsă.'); navigheaza(`/proiecte/${proiectId}?tab=devize`) }, eroare)}>
                  <Button danger icon={<DeleteOutlined />}>Șterge</Button>
                </Popconfirm>
              </>
            )}
            {ciornaExistenta ? (
              <Button icon={<CopyOutlined />} onClick={() => navigheaza(`/proiecte/${proiectId}/devize/${ciornaExistenta.id}`)}>
                Deschide ciorna de lucru nr. {ciornaExistenta.numar}
              </Button>
            ) : editabil && !deLucru && (
              <Tooltip title="Creează o ciornă de lucru din această versiune; la aprobarea ei, aceasta devine înlocuită. Și modificarea directă a unui rând o creează.">
                <Button icon={<CopyOutlined />} loading={versiuneNoua.isPending}
                  onClick={() => versiuneNoua.mutateAsync({ versiuneId }).then((r) => {
                    message.success(`Ciorna de lucru nr. ${r.numar} a fost creată din această versiune.`)
                    navigheaza(`/proiecte/${proiectId}/devize/${r.id}`)
                  }, eroare)}>
                  Modifică (versiune nouă)
                </Button>
              </Tooltip>
            )}
            <Popconfirm title="Aprobi această versiune?"
              description={deLucru && anterioara && anterioara.stare !== 'Inlocuita'
                ? `Devine versiunea în vigoare, iar nr. ${anterioara.numar} devine înlocuită. După aprobare nu se mai modifică.`
                : 'După aprobare nu se mai modifică; schimbările ulterioare se fac într-o versiune nouă.'}
              okText="Aprobă" cancelText="Nu" disabled={!ciorna || !poateAproba}
              onConfirm={() => aproba.mutateAsync(versiuneId).then(() => message.success('Versiunea a fost aprobată.'), eroare)}>
              <Tooltip title={!ciorna ? 'Doar o ciornă se aprobă.' : !poateAproba ? 'Ai nevoie de dreptul ofertare.aprobare.' : undefined}>
                <Button type="primary" icon={<CheckOutlined />} disabled={!ciorna || !poateAproba} loading={aproba.isPending}>
                  Aprobă versiunea
                </Button>
              </Tooltip>
            </Popconfirm>
          </>
        }
      />

      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 16 }} />}
      {data && (
        <Alert
          style={{ marginBottom: 16 }}
          showIcon
          type={ciorna ? 'info' : data.versiune.stare === 'Aprobata' ? 'success' : 'warning'}
          title={deLucru
            ? <>Ciornă de lucru pornită din {anterioara ? <Link to={`/proiecte/${proiectId}/devize/${anterioara.id}`}>versiunea nr. {anterioara.numar}</Link> : 'o versiune anterioară'}: modificările față de ea sunt marcate (<Tag color="gold">modificat</Tag><Tag color="green">nou</Tag>). „Aprobă versiunea” o face versiunea în vigoare{anterioara && anterioara.stare !== 'Inlocuita' ? `, iar nr. ${anterioara.numar} devine înlocuită` : ''}.</>
            : ciornaExistenta
              ? `Pentru această versiune există ciorna de lucru nr. ${ciornaExistenta.numar}; modificările se fac și se văd acolo, aceasta rămâne neschimbată.`
              : ciorna
                ? 'Deviz importat (ciornă): se poate aproba ca atare. Prima modificare a unui rând creează ciorna de lucru (versiune nouă), iar acesta rămâne neschimbat; la aprobarea ciornei, devine înlocuit.'
                : data.versiune.stare === 'Aprobata'
                  ? 'Versiune aprobată: nu se mai modifică. Modificarea unui rând (sau „Modifică”) creează ciorna de lucru; la aprobarea ei, aceasta devine înlocuită.'
                  : 'Versiune înlocuită de una aprobată ulterior; rămâne în istoric, neschimbată.'}
        />
      )}
      {diferente.data && (
        <Card
          size="small"
          style={{ marginBottom: 16 }}
          title={`Modificări față de nr. ${diferente.data.numarVechi}: ${diferente.data.diferente.length === 0 ? 'niciuna încă' : [
            ['Modificat', 'modificate'], ['Adaugat', 'adăugate'], ['Sters', 'scoase'],
          ].map(([t, e]) => `${diferente.data!.diferente.filter((d) => d.tip === t).length} ${e}`).join(', ')}`}
          extra={
            <Space>
              <span>Total fără TVA: {formatLei(diferente.data.totalFaraTvaVechi)} → <b>{formatLei(diferente.data.totalFaraTvaNou)}</b></span>
              <Tag color={diferente.data.totalFaraTvaNou >= diferente.data.totalFaraTvaVechi ? 'red' : 'green'}>
                {diferente.data.totalFaraTvaNou >= diferente.data.totalFaraTvaVechi ? '+' : ''}{formatLei(diferente.data.totalFaraTvaNou - diferente.data.totalFaraTvaVechi)}
              </Tag>
            </Space>
          }
          styles={{ body: { padding: diferente.data.diferente.length ? 0 : undefined } }}
        >
          {diferente.data.diferente.length > 0 && (
            <Table<DiferentaVersiune>
              rowKey="cheieStabila"
              size="small"
              pagination={diferente.data.diferente.length > 10 ? { pageSize: 10, size: 'small' } : false}
              dataSource={diferente.data.diferente}
              columns={[
                {
                  title: '', dataIndex: 'tip', width: 100,
                  render: (t: DiferentaVersiune['tip']) => <Tag color={{ Adaugat: 'green', Modificat: 'gold', Sters: 'red' }[t]}>{{ Adaugat: 'nou', Modificat: 'modificat', Sters: 'scos' }[t]}</Tag>,
                },
                { title: 'Simbol', width: 130, render: (_, d) => <Typography.Text code>{(d.nou ?? d.vechi)!.simbol}</Typography.Text> },
                {
                  title: 'Denumire',
                  render: (_, d) => (
                    <>
                      <div>{(d.nou ?? d.vechi)!.denumire}</div>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{(d.nou ?? d.vechi)!.capitol}</Typography.Text>
                    </>
                  ),
                },
                {
                  title: 'Cantitate', width: 170, className: 'numeric',
                  render: (_, d) => d.vechi && d.nou && d.vechi.cantitate !== d.nou.cantitate
                    ? <>{formatCantitate(d.vechi.cantitate)} → <b>{formatCantitate(d.nou.cantitate)}</b></>
                    : formatCantitate((d.nou ?? d.vechi)!.cantitate),
                },
                {
                  title: 'Preț unitar', width: 190, className: 'numeric',
                  render: (_, d) => d.vechi && d.nou && d.vechi.pretUnitar !== d.nou.pretUnitar
                    ? <>{formatLei(d.vechi.pretUnitar)} → <b>{formatLei(d.nou.pretUnitar)}</b></>
                    : formatLei((d.nou ?? d.vechi)!.pretUnitar),
                },
                {
                  title: 'Diferență valoare', dataIndex: 'diferentaValoare', width: 150, className: 'numeric',
                  render: (v: number) => <Typography.Text type={v > 0 ? 'danger' : v < 0 ? 'success' : undefined}>{v > 0 ? '+' : ''}{formatLei(v)}</Typography.Text>,
                },
              ]}
            />
          )}
        </Card>
      )}

      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12} xl={6}>
          <Card>
            <Statistic title="Total fără TVA" value={rec?.totalFaraTva ?? 0} precision={2} suffix="lei" loading={isLoading} formatter={(v) => formatLei(Number(v))} />
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card>
            <Statistic title="Total cu TVA" value={rec?.totalCuTva ?? 0} precision={2} suffix="lei" loading={isLoading} formatter={(v) => formatLei(Number(v))} />
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card>
            <Statistic title="Articole de deviz" value={nrArticole} loading={isLoading} suffix={data ? `în ${capitole.length} capitole` : ''} />
          </Card>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <Card>
            <Statistic title="Profit" value={rec?.profit ?? 0} precision={2} suffix="lei" loading={isLoading} formatter={(v) => formatLei(Number(v))} />
          </Card>
        </Col>

        <Col xs={24} xxl={17}>
          <Card
            title="Structura devizului"
            styles={{ body: { padding: 0 } }}
            extra={
              <Space wrap>
                <Input.Search allowClear placeholder="Caută simbol, denumire, WBS" style={{ width: 260 }} onChange={(e) => setCauta(e.target.value)} />
                {(textCautat || doarModificari) && <Typography.Text type="secondary">{gasite} articole</Typography.Text>}
                {diferente.data && (
                  <Space size={4}>
                    <Switch size="small" checked={doarModificari} onChange={setDoarModificari} />
                    <span>Doar modificările</span>
                  </Space>
                )}
                <Segmented<Vedere>
                  value={vedere}
                  onChange={setVedere}
                  options={[
                    { value: 'valori', label: 'Valori' },
                    { value: 'preturi', label: 'Prețuri unitare' },
                  ]}
                />
              </Space>
            }
          >
            {data && (
              <Table<Rand>
                key={textCautat || doarModificari ? `filtru-${doarModificari}` : 'tot'}
                rowKey="cheie"
                size="small"
                columns={coloane}
                dataSource={arbore}
                pagination={false}
                scroll={{ x: 1400 }}
                expandable={{ defaultExpandedRowKeys: chei }}
                rowClassName={(r) => `rand-${r.nivel}`}
                onRow={(r) => {
                  const d = r.articol && dupaCheie.get(r.articol.cheieStabila)
                  return {
                    style: r.nivel === 'capitol'
                      ? { background: r.adancime === 0 ? token.colorFillSecondary : token.colorFillQuaternary }
                      : d ? { background: d.tip === 'Adaugat' ? token.colorSuccessBg : token.colorWarningBg } : undefined,
                  }
                }}
              />
            )}
            {isLoading && <Skeleton active style={{ padding: 24 }} />}
          </Card>
        </Col>

        <Col xs={24} xxl={7}>
          <Flex vertical gap={16}>
            <Card title="Recapitulație">
              <Skeleton loading={isLoading} active>
                {rec && data && directe && (
                  <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                    <tbody>
                      {[
                        { e: 'Material', v: directe.material },
                        { e: 'Manoperă', v: directe.manopera },
                        { e: 'Utilaj', v: directe.utilaj },
                        { e: 'Transport', v: directe.transport },
                        ...(areEchipament ? [{ e: 'Echipament', v: directe.echipament }] : []),
                        { e: 'I. Cheltuieli directe pe resurse', v: directe.total, b: true },
                        { e: `Contribuții manoperă (${formatProcent(data.parametri.cotaContributiiManopera)})`, v: rec.contributiiManopera },
                        { e: 'Total cheltuieli directe', v: rec.totalCheltuieliDirecte, b: true },
                        { e: `II. Cheltuieli indirecte (${formatProcent(data.parametri.cotaCheltuieliIndirecte)})`, v: rec.cheltuieliIndirecte },
                        { e: `III. Profit (${formatProcent(data.parametri.cotaProfit)})`, v: rec.profit },
                        { e: 'Total fără TVA', v: rec.totalFaraTva, b: true, linie: true },
                        { e: `TVA (${formatProcent(data.parametri.cotaTva)})`, v: rec.tva },
                        { e: 'TOTAL CU TVA', v: rec.totalCuTva, b: true, linie: true, mare: true },
                      ].map((r) => (
                        <tr key={r.e} style={{ borderTop: r.linie ? `1px solid ${token.colorBorder}` : undefined }}>
                          <td style={{ padding: '6px 0', fontWeight: r.b ? 600 : 400, fontSize: r.mare ? 16 : undefined }}>{r.e}</td>
                          <td className="numeric" style={{ padding: '6px 0', fontWeight: r.b ? 600 : 400, fontSize: r.mare ? 16 : undefined }}>
                            {formatLei(r.v)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </Skeleton>
            </Card>

            <Card title="Structura costurilor directe">
              {directe && directe.total > 0 && (
                <Flex vertical gap={8}>
                  {[
                    { e: 'Material', v: directe.material, c: token.colorPrimary },
                    { e: 'Manoperă', v: directe.manopera, c: token.colorSuccess },
                    { e: 'Utilaj', v: directe.utilaj, c: token.colorWarning },
                    { e: 'Transport', v: directe.transport, c: token.colorInfo },
                    ...(areEchipament ? [{ e: 'Echipament', v: directe.echipament, c: token.colorError }] : []),
                  ].map((r) => (
                    <div key={r.e}>
                      <Flex justify="space-between">
                        <span>{r.e}</span>
                        <span className="numeric">{formatProcent(r.v / directe.total)}</span>
                      </Flex>
                      <Progress percent={(r.v / directe.total) * 100} showInfo={false} strokeColor={r.c} size="small" />
                    </div>
                  ))}
                </Flex>
              )}
            </Card>
          </Flex>
        </Col>
      </Row>

      <Modal
        open={!!deEditat}
        title={deEditat?.articol ? `Modifică articolul ${deEditat.articol.simbol}` : 'Articol nou'}
        okText="Salvează"
        cancelText="Renunță"
        width={720}
        confirmLoading={modificaArticol.isPending || adaugaArticol.isPending}
        onOk={() => void salveazaArticol()}
        onCancel={() => setDeEditat(undefined)}
        forceRender
      >
        <Form form={formArticol} layout="vertical">
          <Row gutter={12}>
            <Col span={6}>
              <Form.Item name="simbol" label="Simbol" rules={[{ required: true, whitespace: true }, { max: 40 }]}><Input /></Form.Item>
            </Col>
            <Col span={18}>
              <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true }, { max: 1000 }]}><Input.TextArea autoSize={{ minRows: 1, maxRows: 3 }} /></Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="unitateMasura" label="UM" rules={[{ required: true, whitespace: true }, { max: 20 }]}><Input /></Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="cantitate" label="Cantitate" rules={[{ required: true, type: 'number', min: 0.000001, message: 'Cantitatea trebuie să fie pozitivă.' }]}>
                <NumarRo style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Typography.Text type="secondary">
            Costul direct unitar, în lei, pe resurse.{deEditat?.articol && deEditat.articol.resurse > 0 ? ' La o cantitate nouă, resursele articolului își păstrează consumul unitar.' : ''}
            {areOferta ? ' Prețul de ofertă se recalculează la cotele și cursul versiunii.' : ''}
          </Typography.Text>
          <Row gutter={12} style={{ marginTop: 8 }}>
            {(['material', 'manopera', 'utilaj', 'transport', 'echipament'] as const).map((k) => (
              <Col span={k === 'echipament' ? 4 : 5} key={k}>
                <Form.Item name={k} label={{ material: 'Material', manopera: 'Manoperă', utilaj: 'Utilaj', transport: 'Transport', echipament: 'Echipament' }[k]}>
                  <NumarRo style={{ width: '100%' }} min={0} />
                </Form.Item>
              </Col>
            ))}
          </Row>
        </Form>
      </Modal>

      <Modal
        open={redenumire !== undefined}
        title="Redenumește versiunea"
        okText="Salvează"
        cancelText="Renunță"
        confirmLoading={redenumeste.isPending}
        onCancel={() => setRedenumire(undefined)}
        onOk={() => redenumire?.trim() && redenumeste.mutateAsync({ versiuneId, denumire: redenumire.trim() })
          .then(() => { message.success('Versiunea a fost redenumită.'); setRedenumire(undefined) }, eroare)}
      >
        <Input value={redenumire} maxLength={300} onChange={(e) => setRedenumire(e.target.value)} />
      </Modal>
    </>
  )
}
