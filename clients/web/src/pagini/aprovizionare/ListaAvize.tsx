import { DeleteOutlined, PaperClipOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Flex, Input, Popconfirm, Select, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useAvize, useStergeAviz } from '../../api/avize'
import type { Aviz } from '../../api/avize'
import { api } from '../../api/client'
import { useProiecte } from '../../api/interogari'

const cantitate = (v: number) => v.toLocaleString('ro-RO', { maximumFractionDigits: 4 })

/**
 * Avizele de însoțire înregistrate (D-40), cu starea mărfii: de recepționat (Stocuri → Recepții), recepționată pe aviz și
 * încă fără factură (stoc cu valoare provizorie) sau facturată (factura legată de aceeași notă de comandă).
 */
export function ListaAvize({ poateCrea, onImport }: { poateCrea: boolean; onImport: () => void }) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const proiecte = useProiecte()
  const [proiectId, setProiectId] = useState<string>()
  const [cauta, setCauta] = useState<string>()
  const avize = useAvize({ proiectId, cauta })
  const sterge = useStergeAviz()

  const deschideOriginal = async (a: Aviz) => {
    try {
      const blob = await api.getBinar(`/api/aprovizionare/proiecte/${a.proiectId}/avize/${a.id}/original`)
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const stergeAviz = async (a: Aviz) => {
    try {
      await sterge.mutateAsync({ proiectId: a.proiectId, id: a.id })
      message.success(`Avizul ${a.numarExtern} a fost șters${a.notaComandaGenerata ? ' (cu nota de comandă generată din el, dacă nu era folosită)' : ''}.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Card
      title="Avize de însoțire a mărfii"
      extra={<Button type="primary" icon={<PlusOutlined />} disabled={!poateCrea} onClick={onImport}>Importă aviz</Button>}
    >
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Select
          allowClear
          showSearch={{ optionFilterProp: 'label' }}
          placeholder="Toate proiectele"
          style={{ width: 280 }}
          value={proiectId}
          onChange={setProiectId}
          options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))}
        />
        <Input.Search allowClear placeholder="Număr aviz, furnizor sau notă de comandă" style={{ width: 320 }} onSearch={(v) => setCauta(v.trim() || undefined)} />
      </Flex>
      {avize.error && <Alert type="error" showIcon title="Avizele nu au putut fi încărcate." description={avize.error.message} />}
      <Table<Aviz>
        rowKey="id"
        size="small"
        loading={avize.isLoading}
        dataSource={avize.data}
        pagination={{ pageSize: 20, hideOnSinglePage: true }}
        expandable={{
          expandedRowRender: (a) => (
            <Table
              size="small"
              rowKey={(_, i) => String(i)}
              pagination={false}
              dataSource={a.linii}
              columns={[
                { title: 'Denumire', dataIndex: 'denumire' },
                { title: 'UM', dataIndex: 'unitateMasura', width: 80 },
                { title: 'Cantitate', dataIndex: 'cantitate', width: 120, align: 'right', render: cantitate },
              ]}
            />
          ),
        }}
        columns={[
          {
            title: 'Aviz',
            key: 'numar',
            render: (_, a) => (
              <>
                <Typography.Text strong>{a.numarExtern}</Typography.Text>
                <br />
                <Typography.Text type="secondary">{a.numar}</Typography.Text>
              </>
            ),
          },
          { title: 'Data', dataIndex: 'data', width: 105, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
          { title: 'Furnizor', dataIndex: 'furnizorDenumire', ellipsis: true },
          { title: 'Proiect', dataIndex: 'proiectCod', width: 110 },
          {
            title: 'Notă de comandă',
            key: 'nota',
            width: 170,
            render: (_, a) => (
              <>
                {a.notaComandaNumar}
                {a.notaComandaGenerata && (
                  <Tooltip title="Generată din aviz: marfa a venit fără comandă în sistem">
                    <Tag style={{ marginInlineStart: 6 }}>din aviz</Tag>
                  </Tooltip>
                )}
              </>
            ),
          },
          { title: 'Materiale', key: 'linii', width: 90, align: 'right', render: (_, a) => a.linii.length },
          {
            title: 'Stoc',
            key: 'stare',
            width: 200,
            render: (_, a) =>
              a.receptii === 0 ? (
                <Tooltip title="Deschide recepțiile">
                  <Tag color="gold" style={{ cursor: 'pointer' }} onClick={() => navigheaza('/stocuri?tab=receptii')}>de recepționat</Tag>
                </Tooltip>
              ) : a.facturat ? (
                <Tag color="green">facturat: {a.facturaNumar}</Tag>
              ) : (
                <Tooltip title="Recepționat pe aviz, fără valoare definitivă: se completează când factura (legată de aceeași notă de comandă) e legată de NIR.">
                  <Tag color="orange">recepționat, fără factură</Tag>
                </Tooltip>
              ),
          },
          {
            title: '',
            key: 'actiuni',
            width: 80,
            render: (_, a) => (
              <Flex gap={4}>
                <Tooltip title="Documentul original">
                  <Button size="small" type="text" icon={<PaperClipOutlined />} onClick={() => void deschideOriginal(a)} />
                </Tooltip>
                <Popconfirm
                  title={`Ștergi avizul ${a.numarExtern}?`}
                  description="Doar cât nu are recepții. Nota de comandă generată din el se șterge și ea, dacă nu o mai folosește nimic."
                  okText="Șterge"
                  cancelText="Nu"
                  onConfirm={() => stergeAviz(a)}
                  disabled={!poateCrea || a.receptii > 0}
                >
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} disabled={!poateCrea || a.receptii > 0} />
                </Popconfirm>
              </Flex>
            ),
          },
        ]}
      />
    </Card>
  )
}
