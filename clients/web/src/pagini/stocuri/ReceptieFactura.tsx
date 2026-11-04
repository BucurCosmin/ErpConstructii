import { Alert, App, AutoComplete, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Space, Spin, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo } from 'react'
import { useArticoleStoc, useGestiuni, usePropunereReceptie, useReceptioneaza } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'

const NOU = '__nou__'

interface LinieValori {
  indexFactura: number
  articolId: string
  articolNouDenumire?: string
  articolNouUm?: string
  cantitateReceptionata: number | null
  observatii?: string
}

interface Valori {
  gestiuneId: string
  data: Dayjs
  observatii?: string
  linii: LinieValori[]
}

const sursaPropunere: Record<string, string> = {
  AliasFurnizor: 'ales la o recepție anterioară de la acest furnizor',
  Alias: 'ales la recepția altui furnizor',
  Denumire: 'aceeași denumire în nomenclator',
}

/**
 * Recepția unei facturi (NIR): gestionarul alege depozitul, verifică ce a primit (diferențele cer observație) și leagă
 * fiecare linie de un material din nomenclator (propus automat; altfel se creează unul nou din textul facturii).
 */
export function ReceptieFactura({ facturaId, onInchide }: { facturaId?: string; onInchide: () => void }) {
  const { message } = App.useApp()
  const propunere = usePropunereReceptie(facturaId)
  const gestiuni = useGestiuni()
  const articole = useArticoleStoc()
  const receptioneaza = useReceptioneaza()
  const [form] = Form.useForm<Valori>()
  const linii = Form.useWatch('linii', form) as LinieValori[] | undefined
  const p = propunere.data

  const depozite = useMemo(() => (gestiuni.data ?? []).filter((g) => g.tip === 'Depozit' && g.activa), [gestiuni.data])

  useEffect(() => {
    if (!p || !depozite.length) return
    form.setFieldsValue({
      gestiuneId: depozite[0].id,
      data: dayjs().isBefore(dayjs(p.data)) ? dayjs(p.data) : dayjs(),
      linii: p.linii.map((l) => ({
        indexFactura: l.indexFactura,
        articolId: l.articol?.id ?? NOU,
        articolNouDenumire: l.denumire,
        articolNouUm: l.unitateMasura,
        cantitateReceptionata: l.cantitate,
      })),
    })
  }, [p, depozite, form])

  const trimite = async (v: Valori) => {
    if (!p) return
    try {
      const nir = await receptioneaza.mutateAsync({
        facturaId: p.facturaId,
        gestiuneId: v.gestiuneId,
        data: v.data.format('YYYY-MM-DD'),
        observatii: v.observatii?.trim() || null,
        // Poziția din factură vine din propunere (rândurile formularului sunt în aceeași ordine); nu e un câmp editabil.
        linii: v.linii.map((l, i) => ({
          indexFactura: p.linii[i].indexFactura,
          articolId: l.articolId === NOU ? null : l.articolId,
          articolNouDenumire: l.articolId === NOU ? (l.articolNouDenumire ?? '').trim() : null,
          articolNouUm: l.articolId === NOU ? (l.articolNouUm ?? '').trim() : null,
          cantitateReceptionata: Number(l.cantitateReceptionata ?? 0),
          observatii: l.observatii?.trim() || null,
        })),
      })
      message.success(`${nir.numar} emis: materialele au intrat în ${nir.gestiune}.`)
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const optiuniArticole = [
    { value: NOU, label: '➕ Material nou (din textul facturii)' },
    ...(articole.data ?? []).filter((a) => a.activ).map((a) => ({ value: a.id, label: `${a.cod} — ${a.denumire} (${a.unitateMasura})` })),
  ]

  return (
    <Modal
      open={!!facturaId}
      onCancel={onInchide}
      width="min(1400px, 96vw)"
      title={p ? `Recepție factura ${p.numar} din ${dayjs(p.data).format('DD.MM.YYYY')} — ${p.furnizorDenumire}` : 'Recepție'}
      okText="Emite NIR-ul"
      cancelText="Renunță"
      onOk={() => form.submit()}
      confirmLoading={receptioneaza.isPending}
      destroyOnHidden
    >
      {!p ? (
        <Flex justify="center" style={{ padding: 48 }}><Spin /></Flex>
      ) : (
        <Form<Valori> form={form} name="receptie" layout="vertical" onFinish={trimite} requiredMark="optional">
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            title="Verifică ce a sosit efectiv. Cantitatea recepționată intră în stoc la prețul facturii; diferențele față de factură cer o observație."
          />
          <Space size="large" wrap>
            <Form.Item name="gestiuneId" label="Depozit" rules={[{ required: true }]} style={{ minWidth: 260 }}>
              <Select options={depozite.map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} />
            </Form.Item>
            <Form.Item name="data" label="Data recepției" rules={[{ required: true }]}>
              <DatePicker format="DD.MM.YYYY" disabledDate={(d) => d.isAfter(dayjs(), 'day') || d.isBefore(dayjs(p.data), 'day')} />
            </Form.Item>
            <Form.Item name="observatii" label="Observații" style={{ minWidth: 360 }}>
              <Input maxLength={1000} />
            </Form.Item>
          </Space>

          <Form.List name="linii">
            {(campuri) => (
              <Table
                size="small"
                pagination={false}
                rowKey="key"
                dataSource={campuri}
                columns={[
                  {
                    title: 'Pe factură',
                    render: (_, c) => {
                      const l = p.linii[c.name]
                      return (
                        <Space orientation="vertical" size={0}>
                          <Typography.Text>{l.denumire}</Typography.Text>
                          <Typography.Text type="secondary">
                            {formatCantitate(l.cantitate)} {l.unitateMasura}{l.pretUnitar !== null && ` × ${formatLei(l.pretUnitar)}`}
                          </Typography.Text>
                        </Space>
                      )
                    },
                  },
                  {
                    title: 'Material în stoc',
                    width: '38%',
                    render: (_, c) => {
                      const l = p.linii[c.name]
                      const esteNou = linii?.[c.name]?.articolId === NOU
                      return (
                        <>
                          <Form.Item name={[c.name, 'articolId']} style={{ marginBottom: esteNou ? 4 : 0 }}
                            extra={l.articol && linii?.[c.name]?.articolId === l.articol.id && `Propus: ${sursaPropunere[l.articol.sursa]}`}>
                            <Select showSearch={{ optionFilterProp: 'label' }} options={optiuniArticole} />
                          </Form.Item>
                          {esteNou && (
                            <Space.Compact style={{ width: '100%' }}>
                              <Form.Item name={[c.name, 'articolNouDenumire']} noStyle rules={[{ required: true, whitespace: true }]}>
                                <Input placeholder="Denumirea în nomenclator" maxLength={300} />
                              </Form.Item>
                              <Form.Item name={[c.name, 'articolNouUm']} noStyle rules={[{ required: true, whitespace: true }]}>
                                <AutoComplete style={{ width: 90 }} placeholder="UM" options={['buc', 'kg', 't', 'm', 'mp', 'mc', 'l', 'set', 'sac', 'bax', 'rola'].map((u) => ({ value: u }))} />
                              </Form.Item>
                            </Space.Compact>
                          )}
                        </>
                      )
                    },
                  },
                  {
                    title: 'Recepționat',
                    width: 140,
                    render: (_, c) => (
                      <Form.Item name={[c.name, 'cantitateReceptionata']} rules={[{ required: true, message: '?' }, { type: 'number', min: 0, message: '≥ 0' }]} style={{ marginBottom: 0 }}>
                        <InputNumber min={0} decimalSeparator="," style={{ width: '100%' }} />
                      </Form.Item>
                    ),
                  },
                  {
                    title: 'Observații',
                    width: 230,
                    render: (_, c) => {
                      const diferenta = (linii?.[c.name]?.cantitateReceptionata ?? 0) - p.linii[c.name].cantitate
                      return (
                        <>
                          {diferenta !== 0 && (
                            <Tooltip title="Diferență față de factură">
                              <Tag color={diferenta < 0 ? 'red' : 'orange'} style={{ marginBottom: 4 }}>{diferenta > 0 ? '+' : ''}{formatCantitate(diferenta)}</Tag>
                            </Tooltip>
                          )}
                          <Form.Item name={[c.name, 'observatii']} style={{ marginBottom: 0 }}
                            rules={[{ required: diferenta !== 0, whitespace: true, message: 'Motivul diferenței' }]}>
                            <Input maxLength={300} placeholder={diferenta !== 0 ? 'ex. lipsă la livrare' : ''} />
                          </Form.Item>
                        </>
                      )
                    },
                  },
                ]}
              />
            )}
          </Form.List>
        </Form>
      )}
    </Modal>
  )
}

