import { CheckCircleOutlined, DeleteOutlined, FileDoneOutlined, InboxOutlined, PlusOutlined, ScanOutlined, WarningOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  AutoComplete,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Descriptions,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Result,
  Row,
  Select,
  Space,
  Spin,
  Tag,
  Tooltip,
  Typography,
  Upload,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useRef, useState } from 'react'
import type { CampCitit, Certitudine } from '../../api/aprovizionare'
import { useImportaFactura, useInterpreteazaFactura } from '../../api/facturi'
import type { FacturaPrimita, InterpretareFactura, PaginaDocument, TipPagina } from '../../api/facturi'
import { ImportNecesarExtern } from '../aprovizionare/ImportNecesarExtern'
import { ImportNotaComanda } from '../aprovizionare/ImportNotaComanda'
import { useProiecte } from '../../api/interogari'
import { formatLei } from '../../util/format'

const UNITATI = ['buc', 'kg', 't', 'm', 'ml', 'mp', 'mc', 'l', 'set', 'sac', 'cut', 'rola', 'pac', 'palet', 'ora', 'zi', 'forfetar']

interface LinieFormular {
  denumire?: string
  unitateMasura?: string
  cantitate?: number | null
  pretUnitar?: number | null
  valoareFaraTva?: number | null
  cotaProcent?: number | null
  /** Materialul intră în stoc (recepție de către gestionar); serviciile se debifează. */
  intraInStoc?: boolean
  citit?: { denumire: CampCitit<string>; cantitate: CampCitit<number>; pretUnitar: CampCitit<number>; valoareFaraTva: CampCitit<number>; cotaTva: CampCitit<number> }
}

interface ValoriFormular {
  proiectId?: string
  numar?: string
  dataEmitere?: Dayjs | null
  dataScadenta?: Dayjs | null
  moneda?: string
  furnizorDenumire?: string
  furnizorCui?: string
  adaugaFurnizor: boolean
  referinta?: string
  observatii?: string
  linii: LinieFormular[]
  totalFaraTva?: number | null
  totalTva?: number | null
  totalDePlata?: number | null
}

/** „De verificat” cât timp valoarea propusă nu a fost schimbată de utilizator. */
function stareCamp<T>(camp: CampCitit<T> | undefined, curenta: unknown, transforma: (v: T) => unknown = (v) => v) {
  if (!camp || camp.certitudine !== 'Nesigur' || camp.valoare === null || curenta !== transforma(camp.valoare)) return {}
  return { validateStatus: 'warning' as const, help: camp.textSursa ? `De verificat. Citit: „${camp.textSursa}”` : 'De verificat' }
}

const etichete: Record<Certitudine, { text: string; culoare: string }> = {
  Sigur: { text: 'citit clar', culoare: 'green' },
  Nesigur: { text: 'de verificat', culoare: 'orange' },
  Lipsa: { text: 'de completat', culoare: 'red' },
}

const rotunjeste = (v: number) => Math.round(v * 100) / 100

/**
 * Importul unei facturi primite: XML e-Factura / ZIP din SPV (citit exact) sau PDF / scanare (citit automat, verificat).
 * Factura se înregistrează pe un proiect, cu originalul arhivat; furnizorul se adaugă în nomenclator dacă lipsește.
 */
export function ImportFactura({ poateCrea, fisierInitial, onImportat }: {
  poateCrea: boolean
  /** Fișierul dat de centrul de import (un document tăiat dintr-un fișier cu mai multe documente). */
  fisierInitial?: File
  onImportat?: (descriere: string) => void
}) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const interpreteaza = useInterpreteazaFactura()
  const importa = useImportaFactura()
  const [form] = Form.useForm<ValoriFormular>()
  const [fisier, setFisier] = useState<File>()
  const [url, setUrl] = useState<string>()
  const [rezultat, setRezultat] = useState<InterpretareFactura>()
  const [creata, setCreata] = useState<FacturaPrimita>()
  const urlCurent = useRef<string | undefined>(undefined)
  const valori = Form.useWatch([], form) as ValoriFormular | undefined
  const [pagina, setPagina] = useState<number>()
  const [importDinDosar, setImportDinDosar] = useState<'NotaComanda' | 'NecesarMateriale'>()
  const citit = useRef<File | undefined>(undefined)

  useEffect(() => () => {
    if (urlCurent.current) URL.revokeObjectURL(urlCurent.current)
  }, [])

  const reseteaza = () => {
    if (urlCurent.current) URL.revokeObjectURL(urlCurent.current)
    urlCurent.current = undefined
    setUrl(undefined)
    setFisier(undefined)
    setRezultat(undefined)
    setCreata(undefined)
    setPagina(undefined)
    setImportDinDosar(undefined)
    form.resetFields()
  }

  const citeste = async (f: File) => {
    reseteaza()
    setFisier(f)
    if (/\.(pdf|png|jpe?g|tiff?)$/i.test(f.name)) {
      const u = URL.createObjectURL(f)
      urlCurent.current = u
      setUrl(u)
    }

    try {
      const r = await interpreteaza.mutateAsync(f)
      setRezultat(r)
      if (r.paginiFactura.length > 0 && r.pagini.length > 1) setPagina(r.paginiFactura[0])
      form.setFieldsValue({
        proiectId: r.proiectPropus?.id,
        numar: r.numar.valoare ?? undefined,
        dataEmitere: r.dataEmitere.valoare ? dayjs(r.dataEmitere.valoare) : null,
        dataScadenta: r.dataScadenta.valoare ? dayjs(r.dataScadenta.valoare) : null,
        moneda: r.moneda.valoare ?? 'RON',
        furnizorDenumire: r.furnizorInNomenclator?.denumire ?? r.furnizorDenumire.valoare ?? undefined,
        furnizorCui: r.furnizorCui.valoare ?? undefined,
        adaugaFurnizor: !r.furnizorInNomenclator,
        referinta: r.referinta ?? undefined,
        linii: r.linii.length
          ? r.linii.map((l) => ({
              denumire: l.denumire.valoare ?? undefined,
              unitateMasura: l.unitateMasura.valoare ?? undefined,
              cantitate: l.cantitate.valoare,
              pretUnitar: l.pretUnitar.valoare,
              valoareFaraTva: l.valoareFaraTva.valoare,
              cotaProcent: l.cotaTva.valoare === null ? null : rotunjeste(l.cotaTva.valoare * 100),
              intraInStoc: true,
              citit: l,
            }))
          : [{}],
        totalFaraTva: r.totalFaraTva.valoare,
        totalTva: r.totalTva.valoare,
        totalDePlata: r.totalDePlata.valoare,
      })
    } catch (e) {
      message.error((e as Error).message)
    }
    return false
  }

  const creeaza = async (v: ValoriFormular) => {
    // La XML, antetul e doar afișat (nu e în formular): valorile vin din interpretare (serverul recitește oricum XML-ul).
    const r = rezultat
    const dataEmitere = v.dataEmitere ?? (r?.dataEmitere.valoare ? dayjs(r.dataEmitere.valoare) : null)
    const proiectId = v.proiectId
    if (!fisier || !proiectId || !dataEmitere || !r) return
    v = {
      ...v,
      numar: v.numar ?? r.numar.valoare ?? '',
      dataScadenta: v.dataScadenta ?? (r.dataScadenta.valoare ? dayjs(r.dataScadenta.valoare) : null),
      moneda: v.moneda ?? r.moneda.valoare ?? 'RON',
      furnizorDenumire: v.furnizorDenumire ?? r.furnizorDenumire.valoare ?? '',
      furnizorCui: v.furnizorCui ?? r.furnizorCui.valoare ?? undefined,
      referinta: v.referinta ?? r.referinta ?? undefined,
      adaugaFurnizor: v.adaugaFurnizor ?? !r.furnizorInNomenclator,
    }
    try {
      const f = await importa.mutateAsync({
        proiectId,
        fisier,
        date: {
          numar: (v.numar ?? '').trim(),
          dataEmitere: dataEmitere.format('YYYY-MM-DD'),
          dataScadenta: v.dataScadenta?.format('YYYY-MM-DD') ?? null,
          moneda: (v.moneda ?? 'RON').trim().toUpperCase(),
          furnizorDenumire: (v.furnizorDenumire ?? '').trim(),
          furnizorCui: v.furnizorCui?.trim() || null,
          adaugaFurnizorInNomenclator: v.adaugaFurnizor,
          linii: v.linii.map((l) => ({
            denumire: (l.denumire ?? '').trim(),
            unitateMasura: (l.unitateMasura ?? '').trim(),
            cantitate: Number(l.cantitate),
            pretUnitar: Number(l.pretUnitar ?? 0),
            valoareFaraTva: Number(l.valoareFaraTva),
            cotaTva: Number(l.cotaProcent ?? 0) / 100,
            intraInStoc: l.intraInStoc !== false,
          })),
          totalFaraTva: Number(v.totalFaraTva ?? 0),
          totalTva: Number(v.totalTva ?? 0),
          totalDePlata: Number(v.totalDePlata ?? 0),
          referinta: v.referinta?.trim() || null,
          observatii: v.observatii?.trim() || null,
        },
      })
      setCreata(f)
      onImportat?.(`Factura ${f.numar}`)
      message.success(`Factura ${f.numar} de la ${f.furnizorDenumire} a fost înregistrată pe ${f.proiectCod}.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  useEffect(() => {
    if (fisierInitial && citit.current !== fisierInitial) {
      citit.current = fisierInitial
      void citeste(fisierInitial)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fisierInitial])

  if (creata) {
    return (
      <Result
        status="success"
        title={`Factura ${creata.numar} a fost înregistrată`}
        subTitle={`${creata.furnizorDenumire} · ${formatLei(creata.totalDePlata)} ${creata.moneda} · proiect ${creata.proiectCod}. Originalul este arhivat.`}
        extra={!fisierInitial && <Button type="primary" onClick={reseteaza}>Importă altă factură</Button>}
      />
    )
  }

  if (!fisier) {
    return (
      <Upload.Dragger
        accept=".xml,.zip,.pdf,.png,.jpg,.jpeg,.tif,.tiff"
        multiple={false}
        showUploadList={false}
        beforeUpload={citeste}
        disabled={!poateCrea}
        style={{ padding: '24px 0' }}
      >
        <p className="ant-upload-drag-icon"><InboxOutlined /></p>
        <p className="ant-upload-text">Trage aici factura sau apasă pentru a o alege</p>
        <p className="ant-upload-hint">
          XML-ul e-Factura sau arhiva ZIP descărcată din SPV (citite exact), ori PDF-ul / scanarea facturii (citite automat, cu verificare).
        </p>
      </Upload.Dragger>
    )
  }

  const esteXml = rezultat?.sursa === 'EFactura'
  const numarare = { Sigur: 0, Nesigur: 0, Lipsa: 0 }
  if (rezultat) {
    for (const c of [rezultat.numar, rezultat.dataEmitere, rezultat.furnizorCui, rezultat.totalDePlata]) numarare[c.certitudine]++
    for (const l of rezultat.linii) for (const c of [l.denumire, l.cantitate, l.valoareFaraTva]) numarare[c.certitudine]++
  }
  const eroareBlocanta = rezultat?.probleme.some((p) => p.severitate === 'Eroare') ?? false

  // Verificarea totalurilor față de linii, pe măsură ce utilizatorul corectează.
  const sumaLinii = rotunjeste((valori?.linii ?? []).reduce((s, l) => s + Number(l?.valoareFaraTva ?? 0), 0))
  const sumaTva = rotunjeste((valori?.linii ?? []).reduce((s, l) => s + rotunjeste(Number(l?.valoareFaraTva ?? 0) * Number(l?.cotaProcent ?? 0) / 100), 0))
  const diferentaFara = valori?.totalFaraTva != null ? rotunjeste(sumaLinii - valori.totalFaraTva) : 0
  const diferentaPlata = valori?.totalFaraTva != null && valori?.totalTva != null && valori?.totalDePlata != null
    ? rotunjeste(valori.totalFaraTva + valori.totalTva - valori.totalDePlata) : 0

  return (
    <Row gutter={16}>
      <Col xs={24} xl={10}>
        <Card
          size="small"
          title={<Space>{esteXml ? <FileDoneOutlined /> : <ScanOutlined />}<Typography.Text ellipsis style={{ maxWidth: 320 }}>{fisier.name}</Typography.Text></Space>}
          extra={!fisierInitial && <Button size="small" onClick={reseteaza}>Altă factură</Button>}
          styles={{ body: { padding: 0 } }}
          style={{ position: 'sticky', top: 80 }}
        >
          {url && /\.pdf$/i.test(fisier.name) ? (
            <iframe key={pagina ?? 0} src={pagina ? `${url}#page=${pagina}` : url} title="Factura originală" style={{ width: '100%', height: 780, border: 0, display: 'block' }} />
          ) : url ? (
            <img src={url} alt="Factura originală" style={{ width: '100%', display: 'block' }} />
          ) : (
            <Result
              icon={<FileDoneOutlined style={{ color: '#2e8b57' }} />}
              title="Factură e-Factura (XML)"
              subTitle="Documentul fiscal oficial: datele au fost citite exact și se păstrează neschimbate. Alege doar proiectul."
            />
          )}
        </Card>
      </Col>

      <Col xs={24} xl={14}>
        {interpreteaza.isPending || !rezultat ? (
          <Card>
            <Flex vertical align="center" gap={12} style={{ padding: 48 }}>
              <Spin size="large" />
              <Typography.Text>Se citește factura…</Typography.Text>
            </Flex>
          </Card>
        ) : (
          <Form<ValoriFormular> form={form} name="importFactura" layout="vertical" onFinish={creeaza} requiredMark="optional" disabled={!poateCrea}>
            {rezultat.facturaExistenta && (
              <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Factura a fost deja înregistrată: ${rezultat.facturaExistenta}.`} />
            )}
            <Alert
              type={eroareBlocanta ? 'error' : numarare.Nesigur + numarare.Lipsa > 0 ? 'warning' : 'success'}
              showIcon
              style={{ marginBottom: 16 }}
              title={
                <Space wrap size={4}>
                  <span>{esteXml ? 'XML e-Factura citit exact:' : rezultat.dinOcr ? 'Scanare citită prin OCR:' : 'PDF citit:'}</span>
                  {(Object.keys(numarare) as Certitudine[]).filter((c) => numarare[c] > 0).map((c) => (
                    <Tag key={c} color={etichete[c].culoare} icon={c === 'Sigur' ? <CheckCircleOutlined /> : <WarningOutlined />}>
                      {numarare[c]} {etichete[c].text}
                    </Tag>
                  ))}
                </Space>
              }
              description={rezultat.probleme.some((p) => p.locatie !== 'pagini') && (
                <ul style={{ margin: '4px 0 0', paddingInlineStart: 18 }}>
                  {rezultat.probleme.filter((p) => p.locatie !== 'pagini').map((p, i) => <li key={i}>{p.mesaj}</li>)}
                </ul>
              )}
            />

            {rezultat.pagini.length > 1 && !fisierInitial && (
              <ContinutDosar
                pagini={rezultat.pagini}
                paginiFactura={rezultat.paginiFactura}
                paginaVizibila={pagina}
                onArata={setPagina}
                onImporta={setImportDinDosar}
                poateCrea={poateCrea}
              />
            )}

            <Card size="small" title="Proiect" style={{ marginBottom: 16 }}>
              <Form.Item
                name="proiectId"
                label="Proiectul pe care se înregistrează costul"
                rules={[{ required: true, message: 'Alege proiectul: orice cost se leagă de un proiect.' }]}
                {...(rezultat.proiectPropus && valori?.proiectId === rezultat.proiectPropus.id && rezultat.proiectPropus.certitudine !== 'Sigur'
                  ? { validateStatus: 'warning' as const, help: 'Propus după referința facturii. Verifică.' } : {})}
                extra={rezultat.referinta && <>Referință în factură: „{rezultat.referinta}”</>}
                style={{ marginBottom: 0 }}
              >
                <Select
                  showSearch={{ optionFilterProp: 'label' }}
                  placeholder="Alege proiectul"
                  disabled={!poateCrea}
                  loading={proiecte.isLoading}
                  options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))}
                />
              </Form.Item>
            </Card>

            <Card size="small" title="Furnizor și factură" style={{ marginBottom: 16 }}>
              {esteXml ? (
                <Descriptions size="small" column={2} items={[
                  { label: 'Furnizor', children: rezultat.furnizorDenumire.valoare },
                  { label: 'CUI', children: rezultat.furnizorCui.valoare },
                  { label: 'Număr', children: rezultat.numar.valoare },
                  { label: 'Data', children: rezultat.dataEmitere.valoare && dayjs(rezultat.dataEmitere.valoare).format('DD.MM.YYYY') },
                  { label: 'Scadența', children: rezultat.dataScadenta.valoare ? dayjs(rezultat.dataScadenta.valoare).format('DD.MM.YYYY') : '—' },
                  { label: 'Moneda', children: rezultat.moneda.valoare },
                ]} />
              ) : (
                <Row gutter={12}>
                  <Col xs={24} md={14}>
                    <Form.Item name="furnizorDenumire" label="Furnizor" rules={[{ required: true, message: 'Completează furnizorul.' }]}
                      {...stareCamp(rezultat.furnizorDenumire, valori?.furnizorDenumire)}>
                      <Input maxLength={300} />
                    </Form.Item>
                  </Col>
                  <Col xs={24} md={10}>
                    <Form.Item name="furnizorCui" label="CUI furnizor" {...stareCamp(rezultat.furnizorCui, valori?.furnizorCui)}>
                      <Input maxLength={20} />
                    </Form.Item>
                  </Col>
                  <Col xs={24} md={8}>
                    <Form.Item name="numar" label="Seria și numărul" rules={[{ required: true, message: 'Completează numărul.' }]}
                      {...stareCamp(rezultat.numar, valori?.numar)}>
                      <Input maxLength={50} />
                    </Form.Item>
                  </Col>
                  <Col xs={12} md={6}>
                    <Form.Item name="dataEmitere" label="Data facturii" rules={[{ required: true, message: 'Completează data.' }]}
                      {...stareCamp(rezultat.dataEmitere, valori?.dataEmitere?.format('YYYY-MM-DD'))}>
                      <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                  <Col xs={12} md={6}>
                    <Form.Item name="dataScadenta" label="Scadența" {...stareCamp(rezultat.dataScadenta, valori?.dataScadenta?.format('YYYY-MM-DD'))}>
                      <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                  <Col xs={12} md={4}>
                    <Form.Item name="moneda" label="Moneda" rules={[{ required: true, len: 3, message: 'Cod de 3 litere' }]}>
                      <Input maxLength={3} />
                    </Form.Item>
                  </Col>
                </Row>
              )}
              {rezultat.furnizorInNomenclator ? (
                <Tag color="green" icon={<CheckCircleOutlined />}>Furnizor existent în nomenclator: {rezultat.furnizorInNomenclator.denumire}</Tag>
              ) : (
                <Form.Item name="adaugaFurnizor" valuePropName="checked" style={{ marginBottom: 0 }}>
                  <Checkbox>Adaugă furnizorul în nomenclatorul de furnizori</Checkbox>
                </Form.Item>
              )}
            </Card>

            <Card size="small" title="Linii" style={{ marginBottom: 16 }}>
              <Row gutter={6} wrap={false} style={{ fontWeight: 600, color: '#4a5568', marginBottom: 4, fontSize: 13 }}>
                <Col flex="auto" style={{ minWidth: 0 }}>Denumire</Col>
                <Col flex="80px">UM</Col>
                <Col flex="100px">Cantitate</Col>
                <Col flex="110px">Preț unitar</Col>
                <Col flex="120px">Valoare</Col>
                <Col flex="76px">TVA %</Col>
                <Col flex="52px"><Tooltip title="Intră în stoc (recepție NIR de către gestionar). Debifează serviciile: transport, taxe, refacturări.">Stoc</Tooltip></Col>
                {!esteXml && <Col flex="32px" />}
              </Row>
              <Form.List name="linii">
                {(campuri, { add, remove }) => (
                  <>
                    {campuri.map(({ key, name }) => {
                      const l = valori?.linii?.[name]
                      const c = l?.citit
                      return (
                        <Row key={key} gutter={6} align="top" wrap={false}>
                          <Col flex="auto" style={{ minWidth: 0 }}>
                            <Form.Item name={[name, 'denumire']} rules={[{ required: true, whitespace: true, message: 'Denumirea?' }]} {...stareCamp(c?.denumire, l?.denumire)}>
                              <Input maxLength={500} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="80px">
                            <Form.Item name={[name, 'unitateMasura']}>
                              <AutoComplete options={UNITATI.map((u) => ({ value: u }))} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="100px">
                            <Form.Item name={[name, 'cantitate']} rules={[{ required: true, message: '?' }]} {...stareCamp(c?.cantitate, l?.cantitate)}>
                              <InputNumber decimalSeparator="," style={{ width: '100%' }} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="110px">
                            <Form.Item name={[name, 'pretUnitar']} {...stareCamp(c?.pretUnitar, l?.pretUnitar)}>
                              <InputNumber decimalSeparator="," style={{ width: '100%' }} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="120px">
                            <Form.Item name={[name, 'valoareFaraTva']} rules={[{ required: true, message: '?' }]} {...stareCamp(c?.valoareFaraTva, l?.valoareFaraTva)}>
                              <InputNumber decimalSeparator="," precision={2} style={{ width: '100%' }} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="76px">
                            <Form.Item name={[name, 'cotaProcent']} rules={[{ required: true, message: '?' }]}
                              {...stareCamp(c?.cotaTva, l?.cotaProcent, (v) => rotunjeste(v * 100))}>
                              <InputNumber min={0} max={100} style={{ width: '100%' }} disabled={esteXml} />
                            </Form.Item>
                          </Col>
                          <Col flex="52px" style={{ textAlign: 'center' }}>
                            <Form.Item name={[name, 'intraInStoc']} valuePropName="checked" initialValue>
                              <Checkbox disabled={false} />
                            </Form.Item>
                          </Col>
                          {!esteXml && (
                            <Col flex="32px">
                              <Tooltip title="Șterge linia">
                                <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1} />
                              </Tooltip>
                            </Col>
                          )}
                        </Row>
                      )
                    })}
                    {!esteXml && <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ intraInStoc: true })} block>Adaugă linie</Button>}
                  </>
                )}
              </Form.List>
            </Card>

            <Card size="small" title="Totaluri" style={{ marginBottom: 16 }}>
              <Row gutter={12}>
                {(['totalFaraTva', 'totalTva', 'totalDePlata'] as const).map((k) => (
                  <Col xs={24} md={8} key={k}>
                    <Form.Item
                      name={k}
                      label={{ totalFaraTva: 'Total fără TVA', totalTva: 'TVA', totalDePlata: 'Total de plată' }[k]}
                      rules={[{ required: true, message: 'Completează totalul.' }]}
                      {...stareCamp(rezultat[k], valori?.[k])}
                    >
                      <InputNumber decimalSeparator="," precision={2} style={{ width: '100%' }} disabled={esteXml} addonAfter={valori?.moneda ?? 'RON'} />
                    </Form.Item>
                  </Col>
                ))}
              </Row>
              {Math.abs(diferentaFara) > 0.01 * Math.max(1, valori?.linii?.length ?? 1) && (
                <Alert type="warning" showIcon title={`Suma liniilor (${formatLei(sumaLinii)}) diferă de totalul fără TVA cu ${formatLei(diferentaFara)}.`} style={{ marginBottom: 8 }} />
              )}
              {Math.abs(diferentaPlata) > 0.01 && (
                <Alert type="warning" showIcon title={`Total fără TVA + TVA diferă de totalul de plată cu ${formatLei(diferentaPlata)} (avans dedus sau greșeală de citire).`} />
              )}
              {Math.abs(diferentaFara) <= 0.01 * Math.max(1, valori?.linii?.length ?? 1) && Math.abs(diferentaPlata) <= 0.01 && (
                <Typography.Text type="success"><CheckCircleOutlined /> Totalurile corespund liniilor (TVA calculat pe linii: {formatLei(sumaTva)}).</Typography.Text>
              )}
            </Card>

            <Form.Item name="observatii" label="Observații">
              <Input.TextArea rows={2} maxLength={2000} />
            </Form.Item>
            {!esteXml && <Form.Item name="referinta" hidden><Input /></Form.Item>}

            <Flex justify="flex-end" gap={8}>
              <Button onClick={reseteaza} disabled={false}>Renunță</Button>
              <Button type="primary" htmlType="submit" loading={importa.isPending} disabled={!poateCrea || !!rezultat.facturaExistenta || eroareBlocanta}>
                Înregistrează factura
              </Button>
            </Flex>
          </Form>
        )}
      </Col>
      <Modal
        open={!!importDinDosar}
        onCancel={() => setImportDinDosar(undefined)}
        footer={null}
        width="min(1500px, 96vw)"
        destroyOnHidden
        title={importDinDosar === 'NotaComanda' ? 'Notă de comandă din același PDF' : 'Necesar de materiale din același PDF'}
      >
        {importDinDosar === 'NotaComanda' && <ImportNotaComanda poateCrea={poateCrea} fisierInitial={fisier} />}
        {importDinDosar === 'NecesarMateriale' && <ImportNecesarExtern poateCrea={poateCrea} fisierInitial={fisier} />}
      </Modal>
    </Row>
  )
}

const culoriTip: Record<TipPagina, string> = {
  Factura: 'blue',
  EFactura: 'geekblue',
  NotaComanda: 'purple',
  NecesarMateriale: 'magenta',
  BonConsum: 'volcano',
  Aviz: 'cyan',
  Certificat: 'green',
  Anexa: 'gold',
  Altele: 'default',
}

/**
 * Ce conține PDF-ul pagină cu pagină (pe hârtie, oamenii țin la un loc factura, nota de comandă, avizul și
 * certificatele). Paginile consecutive de același tip se grupează; nota de comandă și necesarul se pot importa direct.
 */
function ContinutDosar({ pagini, paginiFactura, paginaVizibila, onArata, onImporta, poateCrea }: {
  pagini: PaginaDocument[]
  paginiFactura: number[]
  paginaVizibila?: number
  onArata: (pagina: number) => void
  onImporta: (tip: 'NotaComanda' | 'NecesarMateriale') => void
  poateCrea: boolean
}) {
  const grupuri: { tip: TipPagina; descriere: string; pagini: number[] }[] = []
  for (const p of pagini) {
    const ultim = grupuri[grupuri.length - 1]
    if (ultim && ultim.tip === p.tip && (p.tip === 'Certificat' || p.tip === 'Anexa' || p.tip === 'EFactura' || p.tip === 'Altele')) ultim.pagini.push(p.pagina)
    else grupuri.push({ tip: p.tip, descriere: p.descriere, pagini: [p.pagina] })
  }
  const interval = (ps: number[]) => (ps.length === 1 ? `p. ${ps[0]}` : `p. ${ps[0]}–${ps[ps.length - 1]}`)

  return (
    <Card size="small" title={`Conținutul PDF-ului (${pagini.length} pagini)`} style={{ marginBottom: 16 }}>
      <Space direction="vertical" style={{ width: '100%' }} size={6}>
        {grupuri.map((g) => (
          <Flex key={g.pagini[0]} justify="space-between" align="center" gap={8} wrap>
            <Space size={8}>
              <Button size="small" type={g.pagini.includes(paginaVizibila ?? -1) ? 'primary' : 'default'} onClick={() => onArata(g.pagini[0])}>
                {interval(g.pagini)}
              </Button>
              <Tag color={culoriTip[g.tip]}>{g.descriere}</Tag>
              {g.pagini.some((p) => paginiFactura.includes(p)) && <Typography.Text type="success">factura citită de aici</Typography.Text>}
            </Space>
            {(g.tip === 'NotaComanda' || g.tip === 'NecesarMateriale') && (
              <Button size="small" disabled={!poateCrea} onClick={() => onImporta(g.tip as 'NotaComanda' | 'NecesarMateriale')}>
                {g.tip === 'NotaComanda' ? 'Importă nota de comandă' : 'Importă necesarul'}
              </Button>
            )}
          </Flex>
        ))}
      </Space>
    </Card>
  )
}
