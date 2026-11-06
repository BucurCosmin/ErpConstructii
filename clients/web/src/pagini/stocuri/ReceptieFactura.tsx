import { Alert, App, AutoComplete, Button, Checkbox, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Space, Spin, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo } from 'react'
import { useArticoleStoc, useGestiuni, usePropunereReceptie, useReceptioneaza } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'

const NOU = '__nou__'

interface LinieValori {
  indexFactura: number
  /** Linia intră în acest NIR. */
  selectata: boolean
  articolId: string
  articolNouDenumire?: string
  articolNouUm?: string
  cantitateReceptionata: number | null
  /** Restul liniei vine mai târziu (altă recepție); altfel linia se închide cu ce s-a primit acum. */
  restulVine: boolean
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
 * Recepția unei facturi (NIR), totală sau parțială: gestionarul alege liniile sosite (sau „Recepționează tot”) și
 * cantitățile; ce nu a sosit rămâne de recepționat (alt NIR), dacă nu se închide linia („restul nu mai vine”, cu
 * observație). Fiecare linie se leagă de un material din nomenclator (propus automat; altfel unul nou din textul facturii).
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
  const deschise = p?.linii.filter((l) => !l.inchisa).length ?? 0
  const partial = p?.linii.some((l) => l.receptionat > 0 || l.inchisa) ?? false

  useEffect(() => {
    if (!p || !depozite.length) return
    form.setFieldsValue({
      gestiuneId: depozite[0].id,
      data: dayjs().isBefore(dayjs(p.data)) ? dayjs(p.data) : dayjs(),
      linii: p.linii.map((l) => ({
        indexFactura: l.indexFactura,
        selectata: !l.inchisa,
        articolId: l.articol?.id ?? NOU,
        articolNouDenumire: l.denumire,
        articolNouUm: l.unitateMasura,
        cantitateReceptionata: l.inchisa ? 0 : l.ramas,
        restulVine: true,
      })),
    })
  }, [p, depozite, form])

  /** Toate liniile deschise, cu tot ce a rămas de primit (sau nicio linie). */
  const selecteazaTot = (tot: boolean) => {
    if (!p || !linii) return
    form.setFieldsValue({
      linii: linii.map((l, i) => ({
        ...l,
        selectata: tot && !p.linii[i].inchisa,
        cantitateReceptionata: tot && !p.linii[i].inchisa ? p.linii[i].ramas : l.cantitateReceptionata,
      })),
    })
  }

  const trimite = async (v: Valori) => {
    if (!p) return
    // Poziția din factură vine din propunere (rândurile formularului sunt în aceeași ordine); nu e un câmp editabil.
    const alese = v.linii.map((l, i) => ({ l, linie: p.linii[i] })).filter((x) => x.l.selectata && !x.linie.inchisa)
    if (alese.length === 0) {
      message.warning('Bifează cel puțin o linie sosită (sau „Recepționează tot”).')
      return
    }

    try {
      const nir = await receptioneaza.mutateAsync({
        facturaId: p.facturaId,
        gestiuneId: v.gestiuneId,
        data: v.data.format('YYYY-MM-DD'),
        observatii: v.observatii?.trim() || null,
        linii: alese.map(({ l, linie }) => ({
          indexFactura: linie.indexFactura,
          articolId: l.articolId === NOU ? null : l.articolId,
          articolNouDenumire: l.articolId === NOU ? (l.articolNouDenumire ?? '').trim() : null,
          articolNouUm: l.articolId === NOU ? (l.articolNouUm ?? '').trim() : null,
          cantitateReceptionata: Number(l.cantitateReceptionata ?? 0),
          observatii: l.observatii?.trim() || null,
          restulVine: Number(l.cantitateReceptionata ?? 0) < linie.ramas && l.restulVine,
        })),
      })
      const ramase = deschise - alese.filter(({ l, linie }) => !(Number(l.cantitateReceptionata ?? 0) < linie.ramas && l.restulVine)).length
      message.success(`${nir.numar} emis: materialele au intrat în ${nir.gestiune}.${ramase > 0 ? ` Mai sunt ${ramase} linii de recepționat din factură.` : ''}`)
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
      width="min(1450px, 96vw)"
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
            title={partial
              ? `Factura a fost deja recepționată parțial: mai sunt ${deschise} linii de primit.`
              : 'Bifează liniile sosite și verifică cantitățile; ce nu a sosit încă rămâne de recepționat (alt NIR).'}
            description="Cantitatea recepționată intră în stoc la prețul facturii. Dacă restul unei linii nu mai vine, debifează „restul vine” și scrie motivul."
          />
          <Space size="large" wrap align="end">
            <Form.Item name="gestiuneId" label="Depozit" rules={[{ required: true }]} style={{ minWidth: 260 }}>
              <Select options={depozite.map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} />
            </Form.Item>
            <Form.Item name="data" label="Data recepției" rules={[{ required: true }]}>
              <DatePicker format="DD.MM.YYYY" disabledDate={(d) => d.isAfter(dayjs(), 'day') || d.isBefore(dayjs(p.data), 'day')} />
            </Form.Item>
            <Form.Item name="observatii" label="Observații" style={{ minWidth: 320 }}>
              <Input maxLength={1000} />
            </Form.Item>
            <Form.Item>
              <Space>
                <Button type="primary" ghost onClick={() => selecteazaTot(true)}>Recepționează tot</Button>
                <Button onClick={() => selecteazaTot(false)}>Nicio linie</Button>
              </Space>
            </Form.Item>
          </Space>

          <Form.List name="linii">
            {(campuri) => (
              <Table
                size="small"
                pagination={false}
                rowKey="key"
                dataSource={campuri}
                rowClassName={(c) => (p.linii[c.name].inchisa || !linii?.[c.name]?.selectata ? 'rand-inactiv' : '')}
                columns={[
                  {
                    title: '',
                    width: 40,
                    render: (_, c) => (
                      <Form.Item name={[c.name, 'selectata']} valuePropName="checked" style={{ marginBottom: 0 }}>
                        <Checkbox disabled={p.linii[c.name].inchisa} />
                      </Form.Item>
                    ),
                  },
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
                          {l.inchisa ? (
                            <Tag color="green" style={{ marginTop: 2 }}>recepționată ({formatCantitate(l.receptionat)} {l.unitateMasura})</Tag>
                          ) : l.receptionat > 0 && (
                            <Typography.Text type="warning">primit anterior {formatCantitate(l.receptionat)} · de primit {formatCantitate(l.ramas)} {l.unitateMasura}</Typography.Text>
                          )}
                        </Space>
                      )
                    },
                  },
                  {
                    title: 'Material în stoc',
                    width: '34%',
                    render: (_, c) => {
                      const l = p.linii[c.name]
                      const activa = !l.inchisa && !!linii?.[c.name]?.selectata
                      const esteNou = linii?.[c.name]?.articolId === NOU
                      return (
                        <>
                          <Form.Item name={[c.name, 'articolId']} style={{ marginBottom: esteNou && activa ? 4 : 0 }}
                            extra={activa && l.articol && linii?.[c.name]?.articolId === l.articol.id && `Propus: ${sursaPropunere[l.articol.sursa]}`}>
                            <Select showSearch={{ optionFilterProp: 'label' }} options={optiuniArticole} disabled={!activa} />
                          </Form.Item>
                          {esteNou && activa && (
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
                    title: 'Recepționat acum',
                    width: 150,
                    render: (_, c) => {
                      const activa = !p.linii[c.name].inchisa && !!linii?.[c.name]?.selectata
                      return (
                        <Form.Item name={[c.name, 'cantitateReceptionata']} style={{ marginBottom: 0 }}
                          rules={activa ? [{ required: true, message: '?' }, { type: 'number', min: 0.0001, message: '> 0' }] : []}>
                          <InputNumber min={0} decimalSeparator="," style={{ width: '100%' }} disabled={!activa} addonAfter={p.linii[c.name].unitateMasura} />
                        </Form.Item>
                      )
                    },
                  },
                  {
                    title: 'Restul / observații',
                    width: 260,
                    render: (_, c) => {
                      const l = p.linii[c.name]
                      const v = linii?.[c.name]
                      if (l.inchisa || !v?.selectata) return null
                      const diferenta = (v.cantitateReceptionata ?? 0) - l.ramas
                      const lipsa = diferenta < 0
                      const ceObservatie = (lipsa && !v.restulVine) || diferenta > 0
                      return (
                        <>
                          {diferenta !== 0 && (
                            <Tooltip title={lipsa ? 'Mai puțin decât era de primit' : 'Mai mult decât pe factură'}>
                              <Tag color={lipsa ? (v.restulVine ? 'blue' : 'red') : 'orange'} style={{ marginBottom: 4 }}>
                                {diferenta > 0 ? '+' : ''}{formatCantitate(diferenta)}
                              </Tag>
                            </Tooltip>
                          )}
                          {lipsa && (
                            <Form.Item name={[c.name, 'restulVine']} valuePropName="checked" style={{ marginBottom: 4, display: 'inline-block' }}>
                              <Checkbox>restul vine mai târziu</Checkbox>
                            </Form.Item>
                          )}
                          {(ceObservatie || v.observatii) && (
                            <Form.Item name={[c.name, 'observatii']} style={{ marginBottom: 0 }}
                              rules={[{ required: ceObservatie, whitespace: true, message: lipsa ? 'De ce nu mai vine restul?' : 'Motivul surplusului' }]}>
                              <Input maxLength={300} placeholder={lipsa ? 'ex. lipsă definitivă, anulat de furnizor' : 'ex. surplus livrat'} />
                            </Form.Item>
                          )}
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
