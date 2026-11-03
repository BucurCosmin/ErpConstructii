import { CheckOutlined, DownloadOutlined, PrinterOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, Flex, Progress, Row, Segmented, Skeleton, Statistic, Table, Tag, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { useParams } from 'react-router'
import { useDeviz, useProiect } from '../../api/interogari'
import type { StructuraDeviz, ValoriResurse } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatCantitate, formatLei, formatProcent, stariVersiune, tipuriVersiune } from '../../util/format'

/** Rând din arborele devizului: obiect → categorie → articol. */
interface Rand {
  cheie: string
  nivel: 'obiect' | 'categorie' | 'articol'
  cod: string
  denumire: string
  um?: string
  cantitate?: number
  pretUnitar?: ValoriResurse
  valoare?: ValoriResurse
  total: number
  subcontractata?: boolean
  children?: Rand[]
}

function construiesteArbore(d: StructuraDeviz): Rand[] {
  return d.obiecte.map((o) => ({
    cheie: o.id,
    nivel: 'obiect',
    cod: o.cod,
    denumire: o.denumire,
    total: o.total,
    children: o.categorii.map((c) => ({
      cheie: c.id,
      nivel: 'categorie',
      cod: c.cod,
      denumire: c.denumire,
      total: c.total,
      subcontractata: c.subcontractata,
      children: c.articole.map((a) => ({
        cheie: a.id,
        nivel: 'articol',
        cod: a.simbol,
        denumire: a.denumire,
        um: a.unitateMasura,
        cantitate: a.cantitate,
        pretUnitar: a.pretUnitar,
        valoare: a.valoare,
        total: a.valoare.total,
      })),
    })),
  }))
}

type Vedere = 'valori' | 'preturi'

export function DetaliiDeviz() {
  const { proiectId = '', versiuneId = '' } = useParams()
  const proiect = useProiect(proiectId)
  const { data, isLoading, error } = useDeviz(proiectId, versiuneId)
  const { token } = theme.useToken()
  const [vedere, setVedere] = useState<Vedere>('valori')

  const arbore = useMemo(() => (data ? construiesteArbore(data) : []), [data])
  const chei = useMemo(() => arbore.flatMap((o) => [o.cheie, ...(o.children ?? []).map((c) => c.cheie)]), [arbore])
  const nrArticole = data?.obiecte.reduce((s, o) => s + o.categorii.reduce((x, c) => x + c.articole.length, 0), 0) ?? 0

  const resursa = (titlu: string, k: keyof Omit<ValoriResurse, 'total'>) => ({
    title: titlu,
    width: 130,
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
          {r.subcontractata && (
            <Tag color="purple" style={{ marginInlineStart: 8 }}>
              subcontractat
            </Tag>
          )}
        </>
      ),
    },
    { title: 'UM', dataIndex: 'um', width: 80 },
    { title: 'Cantitate', dataIndex: 'cantitate', width: 110, className: 'numeric', render: (v?: number) => (v === undefined ? '' : formatCantitate(v)) },
    resursa('Material', 'material'),
    resursa('Manoperă', 'manopera'),
    resursa('Utilaj', 'utilaj'),
    resursa('Transport', 'transport'),
    {
      title: vedere === 'valori' ? 'Valoare' : 'Preț unitar',
      width: 150,
      fixed: 'right',
      className: 'numeric',
      render: (_, r) => {
        const v = vedere === 'valori' ? r.total : r.pretUnitar?.total
        return v === undefined ? '' : <Typography.Text strong={r.nivel !== 'articol'}>{formatLei(v)}</Typography.Text>
      },
    },
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
            <Button icon={<PrinterOutlined />} disabled>
              Tipărește
            </Button>
            <Button icon={<DownloadOutlined />} disabled>
              Export Excel
            </Button>
            <Button type="primary" icon={<CheckOutlined />} disabled>
              Aprobă versiunea
            </Button>
          </>
        }
      />

      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 16 }} />}

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
            <Statistic title="Articole de deviz" value={nrArticole} loading={isLoading} suffix={data ? `în ${data.obiecte.length} obiect(e)` : ''} />
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
              <Segmented<Vedere>
                value={vedere}
                onChange={setVedere}
                options={[
                  { value: 'valori', label: 'Valori' },
                  { value: 'preturi', label: 'Prețuri unitare' },
                ]}
              />
            }
          >
            {data && (
              <Table<Rand>
                rowKey="cheie"
                size="small"
                columns={coloane}
                dataSource={arbore}
                pagination={false}
                scroll={{ x: 1300 }}
                expandable={{ defaultExpandedRowKeys: chei }}
                rowClassName={(r) => `rand-${r.nivel}`}
                onRow={(r) => ({
                  style:
                    r.nivel === 'obiect'
                      ? { background: token.colorFillSecondary }
                      : r.nivel === 'categorie'
                        ? { background: token.colorFillQuaternary }
                        : undefined,
                })}
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
    </>
  )
}
