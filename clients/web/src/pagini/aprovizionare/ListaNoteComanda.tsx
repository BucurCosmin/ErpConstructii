import { DownloadOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Form, Input, Row, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { api } from '../../api/client'
import { useNoteComanda } from '../../api/aprovizionare'
import type { FiltreNoteComanda, NotaComanda } from '../../api/aprovizionare'
import { useProiecte } from '../../api/interogari'

const data = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString('ro-RO')

/** Notele de comandă către furnizori (formularul GC 007), din proiectele accesibile utilizatorului. */
export function ListaNoteComanda() {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const [form] = Form.useForm<FiltreNoteComanda>()
  const [filtre, setFiltre] = useState<FiltreNoteComanda>({})
  const note = useNoteComanda(filtre)

  const descarcaOriginal = async (n: NotaComanda) => {
    try {
      const blob = await api.getBinar(`/api/aprovizionare/proiecte/${n.proiectId}/note-comanda/${n.id}/original`)
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank')
      setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Card title="Note de comandă către furnizori">
      {note.error && <Alert type="error" showIcon title="Notele de comandă nu au putut fi încărcate." description={note.error.message} style={{ marginBottom: 16 }} />}
      <Form form={form} layout="vertical" name="filtreNoteComanda" onFinish={(v) => setFiltre(Object.fromEntries(Object.entries(v).filter(([, x]) => x)))}>
        <Row gutter={12}>
          <Col xs={24} md={8}>
            <Form.Item name="proiectId" label="Proiect">
              <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Toate proiectele"
                options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
            </Form.Item>
          </Col>
          <Col xs={24} md={5}>
            <Form.Item name="furnizor" label="Furnizor">
              <Input maxLength={300} allowClear />
            </Form.Item>
          </Col>
          <Col xs={24} md={5}>
            <Form.Item name="material" label="Material">
              <Input maxLength={300} allowClear />
            </Form.Item>
          </Col>
          <Col xs={12} md={3}>
            <Form.Item name="dataDe" label="De la">
              <Input type="date" />
            </Form.Item>
          </Col>
          <Col xs={12} md={3}>
            <Form.Item name="dataPanaLa" label="Până la">
              <Input type="date" />
            </Form.Item>
          </Col>
        </Row>
        <Space style={{ marginBottom: 16 }}>
          <Button type="primary" htmlType="submit">Filtrează</Button>
          <Button onClick={() => { form.resetFields(); setFiltre({}) }}>Resetează</Button>
        </Space>
      </Form>

      <Table<NotaComanda>
        rowKey="id"
        loading={note.isLoading}
        dataSource={note.data}
        pagination={{ pageSize: 15, hideOnSinglePage: true }}
        expandable={{
          expandedRowRender: (n) => (
            <>
              <Space wrap size="large" style={{ marginBottom: 8 }}>
                <Typography.Text><strong>Lucrare:</strong> {n.lucrare ?? '—'}</Typography.Text>
                <Typography.Text><strong>Înregistrată de:</strong> {n.inregistrataDe}, {new Date(n.inregistrataLa).toLocaleString('ro-RO')}</Typography.Text>
              </Space>
              <Table
                rowKey={(_, i) => String(i)}
                size="small"
                pagination={false}
                dataSource={n.linii}
                columns={[
                  { title: 'Nr.', width: 50, render: (_, __, i) => i + 1 },
                  { title: 'Material / produs', dataIndex: 'denumire' },
                  { title: 'UM', dataIndex: 'unitateMasura', width: 80 },
                  { title: 'Cantitate', dataIndex: 'cantitate', width: 110, align: 'right' },
                  { title: 'Termen', dataIndex: 'termen', width: 140 },
                ]}
              />
            </>
          ),
        }}
        columns={[
          {
            title: 'Număr',
            dataIndex: 'numar',
            width: 170,
            render: (numar: string, n) => (
              <Space orientation="vertical" size={0}>
                <span>{numar}</span>
                {n.sursa === 'Import' && (
                  <Tooltip title="Emisă pe hârtie; numărul de pe documentul original">
                    <Tag color="purple" style={{ marginInlineEnd: 0 }}>importată{n.numarExtern ? ` · nr. ${n.numarExtern}` : ''}</Tag>
                  </Tooltip>
                )}
              </Space>
            ),
          },
          { title: 'Data', dataIndex: 'data', width: 110, render: data },
          { title: 'Furnizor', dataIndex: 'furnizorDenumire', ellipsis: true },
          { title: 'Proiect', dataIndex: 'proiectCod', width: 130 },
          { title: 'Materiale', key: 'linii', width: 100, align: 'right', render: (_, n) => n.linii.length },
          {
            title: '',
            key: 'original',
            width: 120,
            render: (_, n) => n.areOriginal && (
              <Button size="small" icon={<DownloadOutlined />} onClick={() => void descarcaOriginal(n)}>Original</Button>
            ),
          },
        ]}
      />
    </Card>
  )
}
