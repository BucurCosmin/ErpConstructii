import { DeleteOutlined, InboxOutlined, PlusOutlined, WarningOutlined } from '@ant-design/icons'
import { Alert, App, Button, Checkbox, Col, DatePicker, Flex, Form, Input, Modal, Row, Select, Space, Switch, Table, Timeline, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useContracteFactura, useCorecteazaFactura, useNoteComandaFactura, useStareCorectie } from '../../api/facturi'
import type { FacturaPrimita } from '../../api/facturi'
import { useProiecte } from '../../api/interogari'
import { NumarRo } from '../../componente/NumarRo'
import { etichetaProiect, formatLei } from '../../util/format'
import { PrevizualizareFactura } from '../stocuri/PrevizualizareFactura'

interface LinieValori {
  denumire: string
  unitateMasura: string
  cantitate: number | null
  pretUnitar: number | null
  valoareFaraTva: number | null
  /** Cota în procente (21), nu fracție. */
  cota: number | null
  intraInStoc: boolean
}

interface Valori {
  numar: string
  dataEmitere: Dayjs
  dataScadenta?: Dayjs | null
  moneda: string
  furnizorDenumire: string
  furnizorCui?: string
  proiectId: string
  referinta?: string
  observatii?: string
  notaComandaId?: string
  contractId?: string
  linii: LinieValori[]
  totalFaraTva: number | null
  totalTva: number | null
  totalDePlata: number | null
  motiv: string
}

const bani = (v: number) => Math.round(v * 100) / 100
const tvaLinie = (l?: Partial<LinieValori>) => bani((l?.valoareFaraTva ?? 0) * (l?.cota ?? 0) / 100)

/**
 * Corectura unei facturi primite înregistrate cu date greșite (citire OCR, tastare): toate câmpurile, liniile și
 * totalurile, cu verificarea din mers (cantitate × preț, suma liniilor = totalul) și originalul alături. Liniile intrate
 * în stoc (NIR activ) se corectează și ele, dar nu se șterg (NIR-ul rămâne cu valorile lui). Motivul e obligatoriu; istoricul rămâne.
 */
export function CorectieFactura({ factura, onInchide }: { factura?: FacturaPrimita; onInchide: () => void }) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const proiecte = useProiecte()
  const stare = useStareCorectie(factura?.id)
  const corecteaza = useCorecteazaFactura()
  const [form] = Form.useForm<Valori>()
  const linii = Form.useWatch('linii', form) as LinieValori[] | undefined
  const totaluri = Form.useWatch([], form) as Partial<Valori> | undefined
  const [arataFactura, setArataFactura] = useState(true)
  const blocate = useMemo(() => new Set(stare.data?.liniiReceptionate ?? []), [stare.data])

  useEffect(() => {
    if (!factura) return
    form.setFieldsValue({
      numar: factura.numar,
      dataEmitere: dayjs(factura.dataEmitere),
      dataScadenta: factura.dataScadenta ? dayjs(factura.dataScadenta) : null,
      moneda: factura.moneda,
      furnizorDenumire: factura.furnizorDenumire,
      furnizorCui: factura.furnizorCui ?? undefined,
      proiectId: factura.proiectId,
      referinta: factura.referinta ?? undefined,
      observatii: factura.observatii ?? undefined,
      notaComandaId: factura.notaComandaId ?? undefined,
      contractId: factura.contractId ?? undefined,
      linii: factura.linii.map((l) => ({
        denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate, pretUnitar: l.pretUnitar,
        valoareFaraTva: l.valoareFaraTva, cota: Math.round(l.cotaTva * 10000) / 100, intraInStoc: l.intraInStoc ?? true,
      })),
      totalFaraTva: factura.totalFaraTva,
      totalTva: factura.totalTva,
      totalDePlata: factura.totalDePlata,
      motiv: '',
    })
  }, [factura, form])

  // Verificările din mers: suma liniilor față de totaluri.
  const sumaBaza = bani((linii ?? []).reduce((s, l) => s + (l?.valoareFaraTva ?? 0), 0))
  const sumaTva = bani((linii ?? []).reduce((s, l) => s + tvaLinie(l), 0))
  const difBaza = bani((totaluri?.totalFaraTva ?? 0) - sumaBaza)
  const difTva = bani((totaluri?.totalTva ?? 0) - sumaTva)
  const difTotal = bani((totaluri?.totalDePlata ?? 0) - (totaluri?.totalFaraTva ?? 0) - (totaluri?.totalTva ?? 0))
  const concordant = Math.abs(difBaza) <= 1 && Math.abs(difTva) <= 1 && Math.abs(difTotal) <= 0.05

  const totaluriDinLinii = () => form.setFieldsValue({ totalFaraTva: sumaBaza, totalTva: sumaTva, totalDePlata: bani(sumaBaza + sumaTva) })

  const proiectAles = Form.useWatch('proiectId', form) as string | undefined
  const note = useNoteComandaFactura(proiectAles ?? factura?.proiectId, factura?.furnizorId, factura?.furnizorDenumire)
  const contracte = useContracteFactura(proiectAles ?? factura?.proiectId, factura?.furnizorId, factura?.furnizorDenumire)

  const salveaza = async (v: Valori) => {
    if (!factura) return
    try {
      const r = await corecteaza.mutateAsync({
        id: factura.id,
        date: {
          numar: v.numar.trim(),
          dataEmitere: v.dataEmitere.format('YYYY-MM-DD'),
          dataScadenta: v.dataScadenta ? v.dataScadenta.format('YYYY-MM-DD') : null,
          moneda: v.moneda.trim().toUpperCase(),
          furnizorDenumire: v.furnizorDenumire.trim(),
          furnizorCui: v.furnizorCui?.trim() || null,
          proiectId: v.proiectId,
          linii: v.linii.map((l) => ({
            denumire: l.denumire.trim(), unitateMasura: l.unitateMasura.trim(), cantitate: Number(l.cantitate ?? 0), pretUnitar: Number(l.pretUnitar ?? 0),
            valoareFaraTva: Number(l.valoareFaraTva ?? 0), cotaTva: Number(l.cota ?? 0) / 100, intraInStoc: l.intraInStoc,
          })),
          totalFaraTva: Number(v.totalFaraTva ?? 0),
          totalTva: Number(v.totalTva ?? 0),
          totalDePlata: Number(v.totalDePlata ?? 0),
          referinta: v.referinta?.trim() || null,
          observatii: v.observatii?.trim() || null,
          motiv: v.motiv.trim(),
          notaComandaId: v.notaComandaId ?? null,
          contractId: v.contractId ?? null,
        },
      })
      message.success(`Factura ${v.numar} a fost corectată.`)
      const avertismente = r.avertismente ?? []
      ;(avertismente.length ? Modal.warning : Modal.info)({
        title: 'Ce s-a schimbat',
        width: 680,
        content: (
          <>
            <p>{r.rezumat}</p>
            {avertismente.length > 0 && (
              <Alert type="warning" showIcon title="De verificat în stoc" description={<ul style={{ paddingLeft: 18, margin: 0 }}>{avertismente.map((a) => <li key={a}>{a}</li>)}</ul>}
                action={<Button size="small" type="primary" onClick={() => { Modal.destroyAll(); navigheaza(`/stocuri?tab=receptii&nir=registru&factura=${factura.id}`) }}>Corectează NIR-ul</Button>} />
            )}
          </>
        ),
      })
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal
      open={!!factura}
      onCancel={onInchide}
      width="98vw"
      style={{ top: 12 }}
      title={factura && `Corectează factura ${factura.numar} — ${factura.furnizorDenumire}`}
      okText="Salvează corectura"
      cancelText="Renunță"
      onOk={() => form.submit()}
      confirmLoading={corecteaza.isPending}
      destroyOnHidden
    >
      <Flex gap={16} align="start">
        <div style={{ flex: 1, minWidth: 0 }}>
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            title="Numerele se scriu cu VIRGULĂ la zecimale și punct la mii: 1.234,56 (punctul tastat e ignorat)."
            description={blocate.size > 0
              ? 'Liniile marcate cu cutie au intrat deja în stoc (NIR). Le poți corecta; cât factura are recepții, liniile existente nu se șterg (se pot adăuga linii noi). NIR-ul și stocul rămân cu cantitatea și prețul recepționate: dacă și acolo e greșit, corectează apoi NIR-ul (Stocuri → Recepții → Registru NIR, ✎).'
              : 'Corectura păstrează istoricul: ce era înainte, ce ai schimbat, cine și de ce.'}
          />
          <Form<Valori> form={form} layout="vertical" onFinish={salveaza} requiredMark="optional" size="small">
            <Row gutter={12}>
              <Col span={5}><Form.Item name="numar" label="Număr" rules={[{ required: true, whitespace: true }]}><Input maxLength={50} /></Form.Item></Col>
              <Col span={4}><Form.Item name="dataEmitere" label="Data" rules={[{ required: true }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
              <Col span={4}><Form.Item name="dataScadenta" label="Scadența"><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
              <Col span={3}><Form.Item name="moneda" label="Moneda" rules={[{ required: true, len: 3 }]}><Input maxLength={3} /></Form.Item></Col>
              <Col span={8}>
                <Form.Item name="proiectId" label="Proiect" rules={[{ required: true }]}>
                  <Select showSearch={{ optionFilterProp: 'label' }}
                    options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
                </Form.Item>
              </Col>
              <Col span={10}><Form.Item name="furnizorDenumire" label="Furnizor" rules={[{ required: true, whitespace: true }]}><Input maxLength={300} /></Form.Item></Col>
              <Col span={5}><Form.Item name="furnizorCui" label="CUI furnizor"><Input maxLength={20} /></Form.Item></Col>
              <Col span={9}><Form.Item name="referinta" label="Referință (comandă, aviz)"><Input maxLength={200} /></Form.Item></Col>
              <Col span={24}>
                <Form.Item name="notaComandaId" label="Nota de comandă a achiziției" extra="Leagă factura de nota de comandă; dacă nota are avize, recepțiile pe aviz primesc apoi prețurile facturii (Stocuri → Recepții).">
                  <Select allowClear={false} showSearch={{ optionFilterProp: 'label' }} placeholder="Fără notă de comandă" loading={note.isLoading}
                    options={note.data?.map((n) => ({
                      value: n.id,
                      label: `${n.acelasiFurnizor ? '★ ' : ''}${n.numar}${n.numarExtern ? ` (nr. ${n.numarExtern})` : ''} · ${n.furnizorDenumire} · ${dayjs(n.data).format('DD.MM.YYYY')}${n.avize > 0 ? ` · ${n.avize} aviz(e)` : ''}`,
                    }))} />
                </Form.Item>
              </Col>
              <Col span={24}>
                <Form.Item name="contractId" label="Contractul cu partenerul (servicii)" extra="Facturile de servicii se leagă de contract: intră în situația lui (facturat, rest, garanție).">
                  <Select allowClear={false} showSearch={{ optionFilterProp: 'label' }} placeholder="Fără contract" loading={contracte.isLoading}
                    options={[
                      ...(factura?.contractId && !contracte.data?.some((c) => c.id === factura.contractId) ? [{ value: factura.contractId, label: factura.contractNumar ?? '' }] : []),
                      ...(contracte.data ?? []).map((c) => ({
                        value: c.id,
                        label: `${c.acelasiPartener ? '★ ' : ''}${c.numar}${c.numarExtern ? ` (nr. ${c.numarExtern})` : ''} · ${c.partenerDenumire} · ${c.obiect}`,
                      })),
                    ]} />
                </Form.Item>
              </Col>
            </Row>

            <Form.List name="linii">
              {(campuri, { add, remove }) => (
                <>
                  <Table
                    size="small"
                    className="tabel-compact"
                    pagination={false}
                    rowKey="key"
                    dataSource={campuri}
                    tableLayout="fixed"
                    columns={[
                      { title: '#', width: 34, render: (_, c) => (blocate.has(c.name) ? <Tooltip title="Intrată în stoc (NIR): se poate corecta, nu se poate șterge"><InboxOutlined style={{ color: '#1677ff' }} /></Tooltip> : c.name + 1) },
                      {
                        title: 'Denumire',
                        render: (_, c) => <Form.Item name={[c.name, 'denumire']} noStyle rules={[{ required: true, whitespace: true }]}><Input /></Form.Item>,
                      },
                      { title: 'UM', width: 70, render: (_, c) => <Form.Item name={[c.name, 'unitateMasura']} noStyle><Input /></Form.Item> },
                      {
                        title: 'Cantitate', width: 105,
                        render: (_, c) => <Form.Item name={[c.name, 'cantitate']} noStyle rules={[{ required: true }]}><NumarRo style={{ width: '100%' }} /></Form.Item>,
                      },
                      {
                        title: 'Preț unitar', width: 115,
                        render: (_, c) => <Form.Item name={[c.name, 'pretUnitar']} noStyle><NumarRo style={{ width: '100%' }} /></Form.Item>,
                      },
                      {
                        title: 'Valoare', width: 150,
                        render: (_, c) => {
                          const l = linii?.[c.name]
                          const calculat = bani((l?.cantitate ?? 0) * (l?.pretUnitar ?? 0))
                          const dif = l && Math.abs(calculat - (l.valoareFaraTva ?? 0)) > Math.max(0.05, Math.abs(calculat) * 0.001)
                          return (
                            <Space.Compact style={{ width: '100%' }}>
                              <Form.Item name={[c.name, 'valoareFaraTva']} noStyle rules={[{ required: true }]}>
                                <NumarRo style={{ width: '100%' }} precision={2} />
                              </Form.Item>
                              {dif && (
                                <Tooltip title={`Cantitate × preț = ${formatLei(calculat)}. Clic pentru a pune această valoare.`}>
                                  <Button icon={<WarningOutlined style={{ color: '#d48806' }} />}
                                    onClick={() => form.setFieldValue(['linii', c.name, 'valoareFaraTva'], calculat)} />
                                </Tooltip>
                              )}
                            </Space.Compact>
                          )
                        },
                      },
                      {
                        title: 'Cotă TVA %', width: 95,
                        render: (_, c) => <Form.Item name={[c.name, 'cota']} noStyle rules={[{ required: true }]}><NumarRo style={{ width: '100%' }} min={0} max={100} /></Form.Item>,
                      },
                      { title: 'TVA', width: 95, className: 'numeric', render: (_, c) => formatLei(tvaLinie(linii?.[c.name])) },
                      {
                        title: 'Stoc', width: 50,
                        render: (_, c) => <Form.Item name={[c.name, 'intraInStoc']} noStyle valuePropName="checked"><Checkbox disabled={blocate.has(c.name)} /></Form.Item>,
                      },
                      {
                        title: '', width: 36,
                        render: (_, c) => !blocate.has(c.name) && campuri.length > 1 && (
                          <Tooltip title={blocate.size > 0 && c.name < (factura?.linii.length ?? 0) ? 'Factura are linii intrate în stoc: liniile existente nu se șterg (NIR-ul le găsește după poziție), doar se corectează.' : undefined}>
                            <Button size="small" type="text" danger icon={<DeleteOutlined />} disabled={blocate.size > 0 && c.name < (factura?.linii.length ?? 0)} onClick={() => remove(c.name)} />
                          </Tooltip>
                        ),
                      },
                    ]}
                  />
                  <Button type="dashed" size="small" icon={<PlusOutlined />} style={{ marginTop: 6 }}
                    onClick={() => add({ denumire: '', unitateMasura: 'buc', cantitate: 1, pretUnitar: 0, valoareFaraTva: 0, cota: 21, intraInStoc: true })}>
                    Adaugă linie
                  </Button>
                </>
              )}
            </Form.List>

            <Row gutter={12} style={{ marginTop: 12 }} align="bottom">
              <Col span={5}><Form.Item name="totalFaraTva" label="Total fără TVA" rules={[{ required: true }]}><NumarRo style={{ width: '100%' }} precision={2} /></Form.Item></Col>
              <Col span={5}><Form.Item name="totalTva" label="Total TVA" rules={[{ required: true }]}><NumarRo style={{ width: '100%' }} precision={2} /></Form.Item></Col>
              <Col span={5}><Form.Item name="totalDePlata" label="Total de plată" rules={[{ required: true }]}><NumarRo style={{ width: '100%' }} precision={2} /></Form.Item></Col>
              <Col span={9}>
                <Form.Item>
                  <Button onClick={totaluriDinLinii}>Totaluri din linii ({formatLei(sumaBaza)} + {formatLei(sumaTva)})</Button>
                </Form.Item>
              </Col>
            </Row>
            {!concordant && (
              <Alert type="warning" showIcon style={{ marginBottom: 12 }}
                title="Liniile nu dau totalurile facturii."
                description={[
                  Math.abs(difBaza) > 1 && `Suma valorilor din linii: ${formatLei(sumaBaza)} (diferență ${formatLei(difBaza)}).`,
                  Math.abs(difTva) > 1 && `Suma TVA din linii: ${formatLei(sumaTva)} (diferență ${formatLei(difTva)}).`,
                  Math.abs(difTotal) > 0.05 && `Total de plată ≠ fără TVA + TVA (diferență ${formatLei(difTotal)}).`,
                ].filter(Boolean).join(' ')} />
            )}
            <Row gutter={12}>
              <Col span={12}><Form.Item name="observatii" label="Observații"><Input.TextArea rows={2} maxLength={1000} /></Form.Item></Col>
              <Col span={12}>
                <Form.Item name="motiv" label="Motivul corecturii" rules={[{ required: true, whitespace: true, message: 'Scrie ce era greșit (rămâne în istoric).' }]}>
                  <Input.TextArea rows={2} maxLength={1000} placeholder="ex. valoarea liniei citită greșit la import (44.429,70, nu 444.297)" />
                </Form.Item>
              </Col>
            </Row>
          </Form>

          {(stare.data?.corectii.length ?? 0) > 0 && (
            <>
              <Typography.Title level={5}>Corecturi anterioare</Typography.Title>
              <Timeline
                items={stare.data!.corectii.map((c) => ({
                  children: (
                    <>
                      <Typography.Text strong>{dayjs(c.corectatLa).format('DD.MM.YYYY HH:mm')} — {c.corectatDe}</Typography.Text>: {c.motiv}
                      <div><Typography.Text type="secondary">{c.rezumat}</Typography.Text></div>
                    </>
                  ),
                }))}
              />
            </>
          )}
        </div>
        {arataFactura && factura && (
          <div style={{ width: '38%', flexShrink: 0, position: 'sticky', top: 0 }}>
            <PrevizualizareFactura facturaId={factura.id} inaltime="78vh" />
          </div>
        )}
      </Flex>
      <div style={{ marginTop: 8 }}>
        <Switch size="small" checked={arataFactura} onChange={setArataFactura} /> <Typography.Text type="secondary">Arată originalul alături</Typography.Text>
      </div>
    </Modal>
  )
}
