import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, DatePicker, Form, Input, InputNumber, Row, Select, Space, Table, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo } from 'react'
import { useProiecte } from '../../api/interogari'
import { useGestiuni, useSoldStoc, useTransfera, useTransferuri } from '../../api/stocuri'
import type { TransferStoc } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'

interface Valori {
  gestiuneSursaId?: string
  proiectId?: string
  data: Dayjs
  observatii?: string
  linii: { articolId?: string; cantitate?: number | null }[]
}

/** Bon de transfer: materiale din depozit în „Materiale în șantier” ale unui proiect, evaluate la CMP. */
export function TransferSantier({ poateCrea }: { poateCrea: boolean }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<Valori>()
  const gestiuni = useGestiuni()
  const proiecte = useProiecte()
  const transfera = useTransfera()
  const transferuri = useTransferuri()
  const sursaId = Form.useWatch('gestiuneSursaId', form) as string | undefined
  const linii = Form.useWatch('linii', form) as Valori['linii'] | undefined
  const sold = useSoldStoc({ gestiuneId: sursaId })

  const depozite = (gestiuni.data ?? []).filter((g) => g.tip === 'Depozit')
  const disponibil = useMemo(() => new Map((sold.data ?? []).map((s) => [s.articolId, s])), [sold.data])

  const trimite = async (v: Valori) => {
    if (!v.gestiuneSursaId || !v.proiectId) return
    try {
      const bt = await transfera.mutateAsync({
        proiectId: v.proiectId,
        gestiuneSursaId: v.gestiuneSursaId,
        data: v.data.format('YYYY-MM-DD'),
        observatii: v.observatii?.trim() || null,
        linii: v.linii.map((l) => ({ articolId: l.articolId!, cantitate: Number(l.cantitate) })),
      })
      message.success(`${bt.numar}: materialele au ajuns în ${bt.gestiuneDestinatie}.`)
      form.setFieldsValue({ linii: [{}], observatii: undefined })
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="large">
      <Card title="Transfer în șantier (bon de transfer)">
        <Form<Valori> form={form} name="transfer" layout="vertical" onFinish={trimite} disabled={!poateCrea}
          initialValues={{ data: dayjs(), linii: [{}], gestiuneSursaId: depozite[0]?.id }}>
          <Row gutter={12}>
            <Col xs={24} md={7}>
              <Form.Item name="gestiuneSursaId" label="Din depozitul" rules={[{ required: true, message: 'Alege depozitul.' }]}>
                <Select options={depozite.map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} loading={gestiuni.isLoading} />
              </Form.Item>
            </Col>
            <Col xs={24} md={9}>
              <Form.Item name="proiectId" label="În șantierul proiectului" rules={[{ required: true, message: 'Alege proiectul.' }]}>
                <Select showSearch={{ optionFilterProp: 'label' }} options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="data" label="Data" rules={[{ required: true }]}>
                <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="observatii" label="Observații">
                <Input maxLength={1000} />
              </Form.Item>
            </Col>
          </Row>

          {sursaId && sold.data?.length === 0 && <Alert type="warning" showIcon title="Depozitul nu are materiale în stoc." style={{ marginBottom: 12 }} />}
          <Form.List name="linii">
            {(campuri, { add, remove }) => (
              <>
                {campuri.map(({ key, name }) => {
                  const s = linii?.[name]?.articolId ? disponibil.get(linii[name].articolId!) : undefined
                  return (
                    <Row key={key} gutter={8} align="top">
                      <Col flex="auto">
                        <Form.Item name={[name, 'articolId']} rules={[{ required: true, message: 'Alege materialul.' }]}>
                          <Select
                            showSearch={{ optionFilterProp: 'label' }}
                            placeholder="Material din depozit"
                            options={(sold.data ?? []).map((x) => ({
                              value: x.articolId,
                              label: `${x.articolCod} — ${x.denumire} · disponibil ${formatCantitate(x.cantitate)} ${x.unitateMasura}`,
                            }))}
                          />
                        </Form.Item>
                      </Col>
                      <Col flex="200px">
                        <Form.Item name={[name, 'cantitate']}
                          rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, max: s?.cantitate, message: s ? `Max. ${formatCantitate(s.cantitate)}` : '> 0' }]}>
                          <InputNumber min={0} decimalSeparator="," style={{ width: '100%' }} addonAfter={s?.unitateMasura} />
                        </Form.Item>
                      </Col>
                      <Col flex="32px">
                        <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1} />
                      </Col>
                    </Row>
                  )
                })}
                <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} block style={{ marginBottom: 16 }}>Adaugă material</Button>
              </>
            )}
          </Form.List>
          <Typography.Text type="secondary" style={{ marginRight: 16 }}>
            Materialele pleacă la costul mediu (CMP) din depozit; gestiunea de șantier a proiectului se creează la primul transfer.
          </Typography.Text>
          <Button type="primary" htmlType="submit" loading={transfera.isPending}>Emite bonul de transfer</Button>
        </Form>
      </Card>

      <Card title="Bonuri de transfer">
        <Table<TransferStoc>
          rowKey="id"
          size="small"
          loading={transferuri.isLoading}
          dataSource={transferuri.data}
          pagination={{ pageSize: 15, hideOnSinglePage: true }}
          expandable={{
            expandedRowRender: (t) => (
              <Table size="small" pagination={false} rowKey="articolCod" dataSource={t.linii}
                columns={[
                  { title: 'Cod', dataIndex: 'articolCod', width: 110 },
                  { title: 'Material', dataIndex: 'denumire' },
                  { title: 'Cantitate', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}`, width: 140, align: 'right' },
                  { title: 'CMP', dataIndex: 'pretUnitar', width: 110, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
                  { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
                ]} />
            ),
          }}
          columns={[
            { title: 'Număr', dataIndex: 'numar', width: 140 },
            { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
            { title: 'Din', dataIndex: 'gestiuneSursa' },
            { title: 'În', dataIndex: 'gestiuneDestinatie' },
            { title: 'Materiale', width: 100, align: 'right', render: (_, t) => t.linii.length },
            { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: (v: number | null) => (v === null ? '—' : formatLei(v)) },
            { title: 'Emis de', dataIndex: 'emisDe', width: 160 },
          ]}
        />
      </Card>
    </Space>
  )
}
