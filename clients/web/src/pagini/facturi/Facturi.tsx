import { DownloadOutlined, StopOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Form, Input, Modal, Select, Space, Table, Tabs, Tag, Tooltip, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { api } from '../../api/client'
import { useAnuleazaFactura, useFacturiPrimite } from '../../api/facturi'
import type { FacturaPrimita, FiltreFacturi, LinieFactura } from '../../api/facturi'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData, formatLei } from '../../util/format'
import { ImportFactura } from './ImportFactura'

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nume
  a.click()
  URL.revokeObjectURL(url)
}

/** Facturile primite: lista (pe proiectele accesibile) și importul din e-Factura / PDF. */
export function Facturi() {
  const { message } = App.useApp()
  const [parametri, setParametri] = useSearchParams()
  const navigheaza = useNavigate()
  const tab = parametri.get('tab') ?? 'primite'
  const permisiuni = usePermisiuni()
  const proiecte = useProiecte()
  const [filtre, setFiltre] = useState<FiltreFacturi>({ stare: 'Inregistrata' })
  const facturi = useFacturiPrimite(filtre)
  const anuleaza = useAnuleazaFactura()
  const [deAnulat, setDeAnulat] = useState<FacturaPrimita>()
  const [formAnulare] = Form.useForm<{ motiv: string }>()
  const poateCrea = permisiuni.data?.includes('financiar.creare') ?? false
  const poateAnula = permisiuni.data?.includes('financiar.anulare') ?? false

  const original = async (f: FacturaPrimita, tip: 'xml' | 'pdf') => {
    try {
      descarca(await api.getBinar(`/api/facturi/primite/${f.id}/original/${tip}`), `${f.numar.replace(/[^a-zA-Z0-9_-]/g, '_')}.${tip === 'xml' && f.sursa === 'EFactura' ? 'xml' : tip}`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const confirmaAnularea = async ({ motiv }: { motiv: string }) => {
    if (!deAnulat) return
    try {
      await anuleaza.mutateAsync({ id: deAnulat.id, motiv: motiv.trim() })
      message.success(`Factura ${deAnulat.numar} a fost anulată.`)
      setDeAnulat(undefined)
      formAnulare.resetFields()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const total = (facturi.data ?? []).filter((f) => f.stare === 'Inregistrata').reduce((s, f) => s + (f.moneda === 'RON' ? f.totalDePlata : 0), 0)

  return (
    <>
      <AntetPagina
        titlu="Facturi primite"
        subtitlu="Facturile furnizorilor, înregistrate pe proiecte: din e-Factura (XML) sau din PDF / scanare."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Facturi' }]}
      />
      <Card styles={{ body: { paddingTop: 4 } }}>
        <Tabs
          activeKey={tab}
          onChange={(t) => setParametri({ tab: t }, { replace: true })}
          items={[
            {
              key: 'primite',
              label: 'Facturi primite',
              children: (
                <>
                  <Form
                    layout="inline"
                    style={{ marginBottom: 16, rowGap: 8 }}
                    initialValues={{ stare: 'Inregistrata' }}
                    onFinish={(v: { proiectId?: string; cauta?: string; perioada?: [Dayjs, Dayjs]; stare?: FacturaPrimita['stare'] }) =>
                      setFiltre({
                        proiectId: v.proiectId,
                        cauta: v.cauta?.trim() || undefined,
                        dataDe: v.perioada?.[0]?.format('YYYY-MM-DD'),
                        dataPanaLa: v.perioada?.[1]?.format('YYYY-MM-DD'),
                        stare: v.stare,
                      })
                    }
                  >
                    <Form.Item name="proiectId">
                      <Select allowClear placeholder="Toate proiectele" style={{ width: 240 }} showSearch={{ optionFilterProp: 'label' }}
                        options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
                    </Form.Item>
                    <Form.Item name="cauta">
                      <Input allowClear placeholder="Furnizor, CUI, număr sau material" style={{ width: 280 }} />
                    </Form.Item>
                    <Form.Item name="perioada">
                      <DatePicker.RangePicker format="DD.MM.YYYY" />
                    </Form.Item>
                    <Form.Item name="stare">
                      <Select allowClear placeholder="Orice stare" style={{ width: 150 }}
                        options={[{ value: 'Inregistrata', label: 'Înregistrate' }, { value: 'Anulata', label: 'Anulate' }]} />
                    </Form.Item>
                    <Button type="primary" htmlType="submit">Filtrează</Button>
                    <Button style={{ marginInlineStart: 'auto' }} onClick={() => navigheaza('/facturi?tab=import')} disabled={!poateCrea}>
                      Importă factură
                    </Button>
                  </Form>

                  {facturi.error && <Alert type="error" showIcon title={(facturi.error as Error).message} style={{ marginBottom: 16 }} />}

                  <Table<FacturaPrimita>
                    rowKey="id"
                    loading={facturi.isLoading}
                    dataSource={facturi.data}
                    pagination={{ pageSize: 20, hideOnSinglePage: true }}
                    summary={() =>
                      (facturi.data?.length ?? 0) > 0 && (
                        <Table.Summary.Row>
                          <Table.Summary.Cell index={0} colSpan={6}><Typography.Text strong>Total facturi înregistrate (RON)</Typography.Text></Table.Summary.Cell>
                          <Table.Summary.Cell index={1} className="numeric"><Typography.Text strong>{formatLei(total)}</Typography.Text></Table.Summary.Cell>
                          <Table.Summary.Cell index={2} colSpan={3} />
                        </Table.Summary.Row>
                      )
                    }
                    expandable={{
                      expandedRowRender: (f) => (
                        <div>
                          <Table<LinieFactura>
                            rowKey={(l) => `${l.denumire}-${l.cantitate}-${l.valoareFaraTva}`}
                            size="small"
                            pagination={false}
                            dataSource={f.linii}
                            columns={[
                              { title: 'Denumire', dataIndex: 'denumire' },
                              { title: 'UM', dataIndex: 'unitateMasura', width: 80 },
                              { title: 'Cantitate', dataIndex: 'cantitate', width: 100, className: 'numeric' },
                              { title: 'Preț unitar', dataIndex: 'pretUnitar', width: 110, className: 'numeric', render: formatLei },
                              { title: 'Valoare', dataIndex: 'valoareFaraTva', width: 120, className: 'numeric', render: formatLei },
                              { title: 'TVA', width: 140, className: 'numeric', render: (_, l) => `${formatLei(l.valoareTva)} (${Math.round(l.cotaTva * 100)}%)` },
                            ]}
                          />
                          <Space wrap size={16} style={{ marginTop: 8 }}>
                            <Typography.Text type="secondary">Fără TVA: {formatLei(f.totalFaraTva)} · TVA: {formatLei(f.totalTva)}</Typography.Text>
                            {f.referinta && <Typography.Text type="secondary">Referință: {f.referinta}</Typography.Text>}
                            <Typography.Text type="secondary">Înregistrată de {f.inregistrataDeNume}, {formatData(f.inregistrataLa)}</Typography.Text>
                            {f.observatii && <Typography.Text type="secondary">Observații: {f.observatii}</Typography.Text>}
                          </Space>
                          {f.motivAnulare && <Alert type="error" showIcon title={`Anulată: ${f.motivAnulare}`} style={{ marginTop: 8 }} />}
                        </div>
                      ),
                    }}
                    columns={[
                      { title: 'Număr', dataIndex: 'numar', width: 140, render: (n: string, f) => <>{n} {f.esteStorno && <Tag color="volcano">storno</Tag>}</> },
                      { title: 'Data', dataIndex: 'dataEmitere', width: 105, render: formatData },
                      {
                        title: 'Furnizor',
                        render: (_, f) => (
                          <div>
                            <Typography.Text strong>{f.furnizorDenumire}</Typography.Text>
                            {f.furnizorCui && <div><Typography.Text type="secondary">CUI {f.furnizorCui}</Typography.Text></div>}
                            {(f.materialeGasite?.length ?? 0) > 0 && (
                              <div style={{ marginTop: 4 }}>
                                {f.materialeGasite!.slice(0, 3).map((m) => <Tag key={m} color="gold" style={{ marginBottom: 2 }}>{m}</Tag>)}
                                {f.materialeGasite!.length > 3 && <Typography.Text type="secondary">+{f.materialeGasite!.length - 3}</Typography.Text>}
                              </div>
                            )}
                          </div>
                        ),
                      },
                      { title: 'Proiect', dataIndex: 'proiectCod', width: 120 },
                      { title: 'Scadența', dataIndex: 'dataScadenta', width: 105, render: formatData, responsive: ['lg'] },
                      { title: 'Total de plată', width: 150, className: 'numeric', render: (_, f) => <Typography.Text strong>{formatLei(f.totalDePlata)} {f.moneda}</Typography.Text> },
                      {
                        title: 'Sursă',
                        width: 120,
                        render: (_, f) => f.stare === 'Anulata'
                          ? <Tag color="red">Anulată</Tag>
                          : f.sursa === 'EFactura' ? <Tag color="blue">e-Factura</Tag> : <Tag>PDF</Tag>,
                      },
                      {
                        title: '',
                        width: 170,
                        render: (_, f) => (
                          <Space size={4}>
                            {f.areXml && <Button size="small" icon={<DownloadOutlined />} onClick={() => void original(f, 'xml')}>XML</Button>}
                            {f.arePdf && <Button size="small" icon={<DownloadOutlined />} onClick={() => void original(f, 'pdf')}>PDF</Button>}
                            {f.stare === 'Inregistrata' && poateAnula && (
                              <Tooltip title="Anulează înregistrarea (ex. proiect greșit)">
                                <Button size="small" danger icon={<StopOutlined />} onClick={() => setDeAnulat(f)} />
                              </Tooltip>
                            )}
                          </Space>
                        ),
                      },
                    ]}
                  />
                </>
              ),
            },
            {
              key: 'import',
              label: 'Import factură',
              children: <ImportFactura poateCrea={poateCrea} />,
            },
          ]}
        />
      </Card>

      <Modal
        title={`Anulează factura ${deAnulat?.numar ?? ''}`}
        open={!!deAnulat}
        onCancel={() => setDeAnulat(undefined)}
        onOk={() => formAnulare.submit()}
        okText="Anulează factura"
        okButtonProps={{ danger: true }}
        cancelText="Renunță"
        confirmLoading={anuleaza.isPending}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">
          Factura rămâne în istoric, marcată ca anulată. Dacă a fost înregistrată pe proiectul greșit, o poți importa din nou pe cel corect.
        </Typography.Paragraph>
        <Form form={formAnulare} layout="vertical" onFinish={confirmaAnularea}>
          <Form.Item name="motiv" label="Motivul anulării" rules={[{ required: true, whitespace: true, message: 'Motivul este obligatoriu.' }]}>
            <Input.TextArea rows={3} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

