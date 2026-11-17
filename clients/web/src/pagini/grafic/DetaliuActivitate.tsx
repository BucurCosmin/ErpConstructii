import { CheckCircleTwoTone, ClockCircleOutlined } from '@ant-design/icons'
import { Descriptions, Drawer, Empty, Progress, Skeleton, Space, Table, Tabs, Tag, Typography } from 'antd'
import { stadii, tipuriActivitate, useDetaliuActivitate } from '../../api/grafic'
import { surseDocument } from '../../api/grafic'
import type { CostAlocat, LegaturaActivitate, ResursaActivitate } from '../../api/grafic'
import { Link } from 'react-router'
import { formatCantitate, formatData, formatLei } from '../../util/format'

const tipuriResursa: Record<ResursaActivitate['tip'], string> = { Manopera: 'manoperă', Material: 'material', Utilaj: 'utilaj', Cheltuiala: 'cheltuială' }

const zileText = (z: number | null | undefined) => (z === null || z === undefined ? '—' : `${z.toLocaleString('ro-RO')} zile`)

/** Fișa unei activități: datele, legăturile, resursele cu costurile, pașii și evoluția ei în actualizările graficului. */
export function DetaliuActivitate({ proiectId, graficId, versiuneId, cod, moneda, costuriErp, laInchidere, laAlegere }: {
  proiectId: string
  graficId: string
  versiuneId: string
  cod: string | undefined
  moneda: string
  /** Costurile ERP alocate direct pe activitate (fără drept de costuri: lipsă). */
  costuriErp?: CostAlocat[]
  laInchidere: () => void
  /** Deschide altă activitate (predecesor / succesor). */
  laAlegere: (cod: string) => void
}) {
  const { data, isLoading } = useDetaliuActivitate(proiectId, graficId, versiuneId, cod)
  const a = data?.activitate

  const coloaneLegaturi = [
    { title: 'Cod', dataIndex: 'cod', width: 170, render: (c: string) => <Typography.Link onClick={() => laAlegere(c)}>{c}</Typography.Link> },
    { title: 'Activitatea', dataIndex: 'denumire', ellipsis: true },
    { title: 'Legătura', width: 90, render: (_: unknown, l: LegaturaActivitate) => `${l.tip}${l.decalajZile ? ` ${l.decalajZile > 0 ? '+' : ''}${l.decalajZile}z` : ''}` },
    { title: 'Start', dataIndex: 'start', width: 95, render: formatData },
    { title: 'Sfârșit', dataIndex: 'sfarsit', width: 95, render: formatData },
    { title: 'Stadiu', dataIndex: 'stadiu', width: 100, render: (s: LegaturaActivitate['stadiu']) => <Tag color={stadii[s].culoare}>{stadii[s].eticheta}</Tag> },
  ]

  return (
    <Drawer open={!!cod} onClose={laInchidere} size={900} destroyOnHidden
      title={a ? <Space wrap>{a.cod}<span style={{ fontWeight: 400 }}>{a.denumire}</span>{a.critica && <Tag color="red">critică</Tag>}</Space> : cod}>
      {isLoading || !data || !a ? (
        <Skeleton active />
      ) : (
        <Space orientation="vertical" size={12} style={{ width: '100%' }}>
          {data.caleWbs && <Typography.Text type="secondary">{data.caleWbs}</Typography.Text>}
          <Descriptions size="small" bordered column={2}
            items={[
              { label: 'Stadiu', children: <><Tag color={stadii[a.stadiu].culoare}>{stadii[a.stadiu].eticheta}</Tag> {tipuriActivitate[a.tip]}</> },
              { label: 'Execuție', children: <Progress percent={a.procent} size="small" style={{ width: 180, margin: 0 }} /> },
              { label: 'Planificat', children: `${formatData(a.startPlanificat)} – ${formatData(a.sfarsitPlanificat)}` },
              { label: 'Actual / prognozat', children: `${formatData(a.start)} – ${formatData(a.sfarsit)}` },
              { label: 'Real', children: a.startReal ? `${formatData(a.startReal)} – ${a.sfarsitReal ? formatData(a.sfarsitReal) : 'în curs'}` : '—' },
              { label: 'Cel mai târziu', children: data.startTarziu ? `${formatData(data.startTarziu)} – ${formatData(data.sfarsitTarziu)}` : '—' },
              { label: 'Durata / rămasă', children: `${zileText(a.durataZile)} / ${zileText(a.durataRamasaZile)}` },
              { label: 'Rezerva totală', children: zileText(a.rezervaTotalaZile) },
              { label: 'Calendar', children: data.calendar ?? '—' },
              { label: 'Constrângere', children: data.constrangere ?? '—' },
              { label: `Cost planificat (${moneda})`, children: formatLei(a.costPlanificat) },
              { label: `Realizat / rămas (${moneda})`, children: `${formatLei(a.costRealizat)} / ${formatLei(a.costRamas)}` },
              ...(Object.keys(a.coduri).length > 0
                ? [{ label: 'Coduri de activitate', span: 2, children: <Space size={[4, 4]} wrap>{Object.entries(a.coduri).map(([t, c]) => <Tag key={t}>{t}: <b>{c}</b></Tag>)}</Space> }]
                : []),
            ]} />

          <Tabs size="small"
            items={[
              {
                key: 'legaturi',
                label: `Legături (${data.predecesori.length + data.succesori.length})`,
                children: (
                  <>
                    <Typography.Text strong>Predecesori</Typography.Text>
                    <Table className="tabel-compact" size="small" pagination={false} rowKey={(l) => `p-${l.cod}-${l.tip}`} dataSource={data.predecesori}
                      columns={coloaneLegaturi} locale={{ emptyText: 'Fără predecesori' }} style={{ marginBottom: 12 }} />
                    <Typography.Text strong>Succesori</Typography.Text>
                    <Table className="tabel-compact" size="small" pagination={false} rowKey={(l) => `s-${l.cod}-${l.tip}`} dataSource={data.succesori}
                      columns={coloaneLegaturi} locale={{ emptyText: 'Fără succesori' }} />
                  </>
                ),
              },
              {
                key: 'resurse',
                label: `Resurse și costuri (${data.resurse.length})`,
                children: data.resurse.length === 0 ? <Empty description="Activitatea nu are resurse sau cheltuieli în grafic." /> : (
                  <Table<ResursaActivitate> className="tabel-compact" size="small" pagination={false} rowKey={(r, i) => `${r.cod}-${i}`} dataSource={data.resurse}
                    scroll={{ x: 1000 }}
                    columns={[
                      { title: 'Cod', dataIndex: 'cod', width: 130 },
                      { title: 'Denumire', dataIndex: 'denumire', ellipsis: true },
                      { title: 'Tip', dataIndex: 'tip', width: 90, render: (t: ResursaActivitate['tip']) => tipuriResursa[t] },
                      { title: 'UM', dataIndex: 'unitateMasura', width: 55 },
                      { title: 'Cant. plan', dataIndex: 'cantitatePlanificata', width: 90, className: 'numeric', render: formatCantitate },
                      { title: 'Cant. real.', dataIndex: 'cantitateRealizata', width: 90, className: 'numeric', render: formatCantitate },
                      { title: 'Preț', dataIndex: 'pretUnitar', width: 100, className: 'numeric', render: formatLei },
                      { title: `Planificat (${moneda})`, dataIndex: 'costPlanificat', width: 120, className: 'numeric', render: formatLei },
                      { title: `Realizat (${moneda})`, dataIndex: 'costRealizat', width: 120, className: 'numeric', render: formatLei },
                      { title: `Rămas (${moneda})`, dataIndex: 'costRamas', width: 110, className: 'numeric', render: formatLei },
                    ]} />
                ),
              },
              ...(costuriErp
                ? [{
                    key: 'erp',
                    label: `Costuri ERP (${costuriErp.length})`,
                    children: costuriErp.length === 0 ? <Empty description="Niciun document alocat pe activitate (Grafic → Alocare costuri)." /> : (
                      <Table<CostAlocat> className="tabel-compact" size="small" pagination={false} rowKey={(x) => `${x.documentId}-${x.linieIndex}`}
                        dataSource={costuriErp}
                        summary={(r) => (
                          <Table.Summary.Row>
                            <Table.Summary.Cell index={0} colSpan={4}><b>Total</b></Table.Summary.Cell>
                            <Table.Summary.Cell index={4} align="right"><b>{formatLei(r.reduce((s, x) => s + x.lei, 0))}</b></Table.Summary.Cell>
                            <Table.Summary.Cell index={5} align="right"><b>{formatLei(r.reduce((s, x) => s + (x.monedaGrafic ?? 0), 0))}</b></Table.Summary.Cell>
                          </Table.Summary.Row>
                        )}
                        columns={[
                          {
                            title: 'Document', width: 170,
                            render: (_, x) => <>{surseDocument[x.sursa]} {x.sursa === 'Factura' ? <Link to={`/facturi?factura=${x.documentId}`}>{x.documentNumar}</Link> : x.documentNumar}</>,
                          },
                          { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
                          { title: 'Linia', dataIndex: 'linie', ellipsis: true },
                          { title: '%', dataIndex: 'procent', width: 60, className: 'numeric', render: (v: number) => v.toLocaleString('ro-RO') },
                          { title: 'Lei', dataIndex: 'lei', width: 110, className: 'numeric', render: formatLei },
                          {
                            title: moneda, dataIndex: 'monedaGrafic', width: 110, className: 'numeric',
                            render: (v: number | null, x) => <span title={x.cursGrafic ? `curs BNR ${x.cursGrafic} din ${formatData(x.dataCurs)}` : undefined}>{v === null ? '—' : formatLei(v)}</span>,
                          },
                        ]} />
                    ),
                  }]
                : []),
              {
                key: 'pasi',
                label: `Pași (${data.pasi.length})`,
                children: data.pasi.length === 0 ? <Empty description="Activitatea nu are pași de execuție." /> : (
                  <ul style={{ listStyle: 'none', paddingLeft: 0 }}>
                    {data.pasi.map((p, i) => (
                      <li key={i} style={{ padding: '3px 0' }}>
                        {p.finalizat ? <CheckCircleTwoTone twoToneColor="#52c41a" /> : <ClockCircleOutlined style={{ color: '#999' }} />} {p.denumire}
                      </li>
                    ))}
                  </ul>
                ),
              },
              {
                key: 'istoric',
                label: `Istoric (${data.istoric.length})`,
                children: (
                  <Table className="tabel-compact" size="small" pagination={false} rowKey="versiune" dataSource={data.istoric}
                    columns={[
                      { title: 'Actualizarea', dataIndex: 'versiune', width: 100, render: (n: number) => `nr. ${n}` },
                      { title: 'Data', dataIndex: 'dataActualizare', width: 100, render: formatData },
                      { title: 'Execuție', dataIndex: 'procent', width: 90, className: 'numeric', render: (p: number) => `${p.toLocaleString('ro-RO')}%` },
                      { title: 'Start', dataIndex: 'start', width: 100, render: formatData },
                      { title: 'Sfârșit', dataIndex: 'sfarsit', width: 100, render: formatData },
                      { title: 'Stadiu', dataIndex: 'stadiu', render: (s: LegaturaActivitate['stadiu']) => <Tag color={stadii[s].culoare}>{stadii[s].eticheta}</Tag> },
                    ]} />
                ),
              },
            ]} />
        </Space>
      )}
    </Drawer>
  )
}
