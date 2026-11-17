import { Alert, Modal, Skeleton, Table, Tabs, Tag, Typography } from 'antd'
import { stadii, useComparatieGrafic } from '../../api/grafic'
import type { ActivitateComparata, ModificareActivitate } from '../../api/grafic'
import { formatData } from '../../util/format'
import { zile } from './ImportGrafic'

const decalaj = (z: number | null) =>
  z === null ? <Typography.Text type="secondary">—</Typography.Text> : <Typography.Text type={z > 0 ? 'danger' : 'success'}>{zile(z)}</Typography.Text>

/** Ce s-a schimbat între actualizarea aleasă și cea anterioară: activități întârziate, modificate, noi și șterse. */
export function ComparatieGrafic({ proiectId, graficId, versiuneId, numar, laInchidere, laAlegere }: {
  proiectId: string
  graficId: string
  versiuneId: string | undefined
  numar: number
  laInchidere: () => void
  laAlegere: (cod: string) => void
}) {
  const { data, isLoading, error } = useComparatieGrafic(proiectId, graficId, versiuneId)
  const codLink = (c: string) => <Typography.Link onClick={() => laAlegere(c)}>{c}</Typography.Link>
  const coloaneModificari = [
    { title: 'Cod', dataIndex: 'cod', width: 170, render: codLink },
    {
      title: 'Activitatea', dataIndex: 'denumire', ellipsis: true,
      render: (d: string, m: ModificareActivitate) => (m.denumireAnterioara ? <span title={`Înainte: ${m.denumireAnterioara}`}>{d} <Tag>redenumită</Tag></span> : d),
    },
    { title: 'Start', dataIndex: 'decalajStartZile', width: 95, render: decalaj, sorter: (a: ModificareActivitate, b: ModificareActivitate) => (a.decalajStartZile ?? 0) - (b.decalajStartZile ?? 0) },
    {
      title: 'Sfârșit', dataIndex: 'decalajSfarsitZile', width: 95, render: decalaj, defaultSortOrder: 'descend' as const,
      sorter: (a: ModificareActivitate, b: ModificareActivitate) => (a.decalajSfarsitZile ?? 0) - (b.decalajSfarsitZile ?? 0),
    },
    {
      title: 'Execuție', width: 120, className: 'numeric',
      render: (_: unknown, m: ModificareActivitate) => (m.procentAnterior === m.procent ? `${m.procent}%` : `${m.procentAnterior}% → ${m.procent}%`),
    },
    {
      title: 'Stadiu', width: 190,
      render: (_: unknown, m: ModificareActivitate) => (m.stadiuAnterior === m.stadiu
        ? <Tag color={stadii[m.stadiu].culoare}>{stadii[m.stadiu].eticheta}</Tag>
        : <><Tag>{stadii[m.stadiuAnterior].eticheta}</Tag>→ <Tag color={stadii[m.stadiu].culoare}>{stadii[m.stadiu].eticheta}</Tag></>),
    },
  ]
  const coloaneSimple = [
    { title: 'Cod', dataIndex: 'cod', width: 170 },
    { title: 'Activitatea', dataIndex: 'denumire', ellipsis: true },
    { title: 'WBS', dataIndex: 'codWbs', width: 220, ellipsis: true },
  ]
  const tabel = <T extends ModificareActivitate | ActivitateComparata>(date: T[], coloane: object[]) => (
    <Table<T> className="tabel-compact" size="small" rowKey="cod" dataSource={date} columns={coloane as never} scroll={{ y: 460 }}
      pagination={date.length > 200 ? { pageSize: 200, size: 'small', showSizeChanger: false } : false} />
  )

  return (
    <Modal open={!!versiuneId} onCancel={laInchidere} footer={null} width="min(1200px, 96vw)" destroyOnHidden
      title={`Actualizarea nr. ${numar} față de nr. ${numar - 1}`}>
      {error ? <Alert type="info" showIcon title={(error as Error).message} /> : isLoading || !data ? <Skeleton active /> : (
        <>
          <Alert style={{ marginBottom: 12 }} type={data.decalajSfarsitProiectZile && data.decalajSfarsitProiectZile > 0 ? 'warning' : 'info'} showIcon
            title={<>Sfârșitul graficului: {formatData(data.sfarsitAnterior)} → {formatData(data.sfarsit)}
              {data.decalajSfarsitProiectZile ? <b> ({zile(data.decalajSfarsitProiectZile)})</b> : ' (neschimbat)'}; execuția {data.procentAnterior}% → {data.procent}%</>} />
          <Tabs size="small"
            items={[
              { key: 'intarziate', label: `Sfârșit amânat (${data.intarziate})`, children: tabel(data.modificate.filter((m) => (m.decalajSfarsitZile ?? 0) > 0), coloaneModificari) },
              { key: 'modificate', label: `Toate modificările (${data.modificate.length})`, children: tabel(data.modificate, coloaneModificari) },
              { key: 'noi', label: `Noi (${data.noi.length})`, children: tabel(data.noi, coloaneSimple) },
              { key: 'sterse', label: `Șterse (${data.sterse.length})`, children: tabel(data.sterse, coloaneSimple) },
            ]} />
        </>
      )}
    </Modal>
  )
}
