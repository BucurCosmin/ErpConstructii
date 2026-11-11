import { ShoppingCartOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Input, Modal, Select, Space, Table, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useNoteComanda } from '../../api/aprovizionare'
import type { LinieNotaComanda, NotaComanda } from '../../api/aprovizionare'
import { useProiecte } from '../../api/interogari'
import { etichetaProiect, formatData } from '../../util/format'
import { NotaComandaNoua } from './NotaComandaNoua'

const cantitate = (v: number) => v.toLocaleString('ro-RO', { maximumFractionDigits: 4 })
const faraDiacritice = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase()

/**
 * Necesar de achiziție (D-37), pentru achiziții: notele de comandă acceptate, încă fără furnizor. De aici se aleg furnizorul
 * și condițiile și se emite comanda — toată nota la un furnizor sau împărțită pe mai mulți (ce nu se comandă rămâne aici).
 */
export function NecesarAchizitie({ poateCrea }: { poateCrea: boolean }) {
  const proiecte = useProiecte()
  const [proiectId, setProiectId] = useState<string>()
  const [cauta, setCauta] = useState('')
  const note = useNoteComanda({ stare: 'Acceptata', proiectId })
  const [deComandat, setDeComandat] = useState<NotaComanda>()

  const lista = useMemo(() => {
    const t = faraDiacritice(cauta.trim())
    return (note.data ?? []).filter((n) => !t || n.linii.some((l) => faraDiacritice(l.denumire).includes(t)) || faraDiacritice(n.numar + ' ' + (n.necesarNumar ?? '')).includes(t))
  }, [note.data, cauta])

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Alert type="info" showIcon
        title="Notele de comandă acceptate, generate din necesarele șantierelor, care așteaptă furnizorul."
        description="Alegi furnizorul și emiți comanda (Word GC 007 / PDF). Poți comanda la un furnizor doar o parte din materiale: restul rămâne aici, pentru alt furnizor." />
      <Space wrap>
        <Select allowClear placeholder="Toate proiectele" style={{ width: 260 }} showSearch={{ optionFilterProp: 'label' }} value={proiectId} onChange={setProiectId}
          options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
        <Input.Search allowClear placeholder="Material sau număr (NC / NM)" style={{ width: 260 }} onSearch={setCauta} onChange={(e) => !e.target.value && setCauta('')} />
      </Space>
      {note.error && <Alert type="error" showIcon title={(note.error as Error).message} />}
      <Card size="small" title={`${lista.length} note de comandă de trimis la furnizori · ${lista.reduce((s, n) => s + n.linii.length, 0)} materiale`}>
        <Table<NotaComanda>
          rowKey="id"
          size="small"
          className="tabel-compact"
          tableLayout="fixed"
          scroll={{ x: 950 }}
          loading={note.isLoading}
          dataSource={lista}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nimic de comandat: notele acceptate apar aici." /> }}
          pagination={{ defaultPageSize: 25, hideOnSinglePage: true, size: 'small' }}
          expandable={{
            defaultExpandAllRows: true,
            expandedRowRender: (n) => (
              <Table<LinieNotaComanda>
                rowKey={(_, i) => String(i)} size="small" className="tabel-compact" tableLayout="fixed" pagination={false} dataSource={n.linii}
                columns={[
                  { title: 'Nr.', width: 45, render: (_, __, i) => i + 1 },
                  { title: 'Material / produs', dataIndex: 'denumire', ellipsis: { showTitle: true } },
                  { title: 'UM', dataIndex: 'unitateMasura', width: 70 },
                  { title: 'Cantitate', dataIndex: 'cantitate', width: 100, className: 'numeric', render: cantitate },
                  { title: 'Observații', dataIndex: 'observatii', width: 220, ellipsis: { showTitle: true } },
                ]}
              />
            ),
          }}
          columns={[
            { title: 'Notă', dataIndex: 'numar', width: 130 },
            { title: 'Necesar', dataIndex: 'necesarNumar', width: 130 },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 110, ellipsis: { showTitle: true } },
            { title: 'Lucrare', dataIndex: 'lucrare', ellipsis: { showTitle: true } },
            {
              title: 'Acceptată', width: 190,
              render: (_, n) => <Typography.Text type="secondary">{n.acceptataDe}{n.acceptataLa ? `, ${formatData(n.acceptataLa)}` : ''}</Typography.Text>,
            },
            { title: 'Mat.', width: 55, className: 'numeric', render: (_, n) => n.linii.length },
            {
              title: '', width: 170, fixed: 'right',
              render: (_, n) => (
                <Button size="small" type="primary" icon={<ShoppingCartOutlined />} disabled={!poateCrea} onClick={() => setDeComandat(n)}>
                  Comandă la furnizor
                </Button>
              ),
            },
          ]}
        />
      </Card>

      <Modal open={!!deComandat} onCancel={() => setDeComandat(undefined)} footer={null} width="min(1150px, 96vw)" style={{ top: 24 }} destroyOnHidden
        title={deComandat && `Comandă la furnizor — ${deComandat.numar}${deComandat.necesarNumar ? ` (necesar ${deComandat.necesarNumar})` : ''}, ${deComandat.proiectCod}`}>
        {deComandat && <NotaComandaNoua key={deComandat.id} poateCrea={poateCrea} dinNota={deComandat} />}
      </Modal>
    </Space>
  )
}
