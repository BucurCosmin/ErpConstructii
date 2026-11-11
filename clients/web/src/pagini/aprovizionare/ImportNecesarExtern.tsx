import { DeleteOutlined, FileWordOutlined, InboxOutlined, PlusOutlined, ScanOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  AutoComplete,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Flex,
  Form,
  Input,
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
import { useImportaNecesar, useInterpreteazaNecesar, useNoteComanda } from '../../api/aprovizionare'
import type { CampCitit, Certitudine, InterpretareNecesar, NecesarMateriale } from '../../api/aprovizionare'
import { useProiecte } from '../../api/interogari'
import { eticheteCertitudine, stareCamp, UNITATI } from './campuriCitite'
import { NumarRo } from '../../componente/NumarRo'
import { CampDinUrma } from '../../componente/DocumentDinUrma'
import { useSantiere } from '../../api/proiecte'
import { potrivesteSolicitant, SelectSolicitant, solicitantDin } from '../../componente/SelectSolicitant'
import { etichetaProiect } from '../../util/format'

interface LinieFormular {
  denumire?: string
  unitateMasura?: string
  cantitate?: number | null
  observatii?: string
  /** Valorile citite din document și certitudinea lor (nu se trimit la server). */
  citit?: {
    rand: number
    denumire: CampCitit<string>
    unitateMasura: CampCitit<string>
    cantitate: CampCitit<number>
  }
}

interface ValoriFormular {
  proiectId?: string
  numarExtern?: string
  dataDocument?: Dayjs | null
  compartimentSantier?: string
  /** D-38: șantierul proiectului / compartimentul firmei, propus după textul citit. */
  solicitant?: string
  /** D-45: necesar vechi din arhivă (intră aprobat, fără nota „de acceptat”). */
  dinUrma?: boolean
  /** D-45: notele de comandă importate deja, de legat de acest necesar. */
  noteComandaIds?: string[]
  linii: LinieFormular[]
}

/**
 * Importul unui necesar de materiale emis în afara sistemului (PDF scanat, poză, Word):
 * sistemul citește documentul, propune datele, iar utilizatorul le verifică și le completează înainte de creare.
 * Necesarul din urmă (D-45) intră aprobat și se poate lega de notele de comandă importate înaintea lui.
 */
export function ImportNecesarExtern({ poateCrea, fisierInitial, onImportat, dinUrma }: {
  poateCrea: boolean
  fisierInitial?: File
  onImportat?: (descriere: string) => void
  /** Bifa „din urmă” propusă (din centrul de import). */
  dinUrma?: boolean
}) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const interpreteaza = useInterpreteazaNecesar()
  const importa = useImportaNecesar()
  const [form] = Form.useForm<ValoriFormular>()
  const [fisier, setFisier] = useState<File>()
  const [url, setUrl] = useState<string>()
  const [rezultat, setRezultat] = useState<InterpretareNecesar>()
  const [creata, setCreata] = useState<NecesarMateriale>()
  const urlCurent = useRef<string | undefined>(undefined)

  const valori = Form.useWatch([], form) as ValoriFormular | undefined
  const linii = Form.useWatch('linii', form) as LinieFormular[] | undefined
  // Notele importate fără necesar, pe proiect: candidatele de legat de un necesar din urmă.
  const note = useNoteComanda({ proiectId: valori?.proiectId, sursa: 'Import' }, !!valori?.proiectId && !!valori?.dinUrma)
  const noteFaraNecesar = (note.data ?? []).filter((n) => n.stare === 'Emisa' && !n.necesarId)

  // Șantierul proiectului ales, propus după textul de pe document (D-38).
  const santiere = useSantiere(valori?.proiectId)
  useEffect(() => {
    if (!rezultat || form.getFieldValue('solicitant') || !santiere.data) return
    const propus = potrivesteSolicitant(rezultat.compartimentSantier.valoare, santiere.data)
    if (propus) form.setFieldValue('solicitant', propus)
  }, [rezultat, santiere.data, form])

  // Previzualizarea originalului se eliberează la plecarea din pagină.
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
    form.resetFields()
  }

  const citeste = async (f: File) => {
    reseteaza()
    setFisier(f)
    if (!f.name.toLowerCase().endsWith('.docx')) {
      const u = URL.createObjectURL(f)
      urlCurent.current = u
      setUrl(u)
    }

    try {
      const r = await interpreteaza.mutateAsync(f)
      setRezultat(r)
      form.setFieldsValue({
        proiectId: r.proiectPropus?.id,
        dinUrma: !!dinUrma,
        numarExtern: r.numarExtern.valoare ?? undefined,
        dataDocument: r.data.valoare ? dayjs(r.data.valoare) : null,
        compartimentSantier: r.compartimentSantier.valoare ?? undefined,
        solicitant: undefined,
        linii: r.linii.length
          ? r.linii.map((l) => ({
              denumire: l.denumire.valoare ?? undefined,
              unitateMasura: l.unitateMasura.valoare ?? undefined,
              cantitate: l.cantitate.valoare,
              observatii: l.observatii.valoare ?? undefined,
              citit: { rand: l.rand, denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate },
            }))
          : [{}],
      })
    } catch (e) {
      message.error((e as Error).message)
    }
    return false
  }

  // Deschis din importul unei facturi: același PDF (dosarul scanat), citit direct, fără o nouă alegere a fișierului.
  const citit = useRef<File | undefined>(undefined)
  useEffect(() => {
    if (fisierInitial && citit.current !== fisierInitial) {
      citit.current = fisierInitial
      void citeste(fisierInitial)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fisierInitial])

  const creeaza = async (v: ValoriFormular) => {
    if (!fisier || !v.proiectId || !v.dataDocument) return
    try {
      const necesar = await importa.mutateAsync({
        proiectId: v.proiectId,
        fisier,
        date: {
          numarExtern: v.numarExtern?.trim() || null,
          dataDocument: v.dataDocument.format('YYYY-MM-DD'),
          compartimentSantier: v.compartimentSantier?.trim() || null,
          ...solicitantDin(v.solicitant),
          linii: v.linii.map((l) => ({
            denumire: (l.denumire ?? '').trim(),
            unitateMasura: (l.unitateMasura ?? '').trim(),
            cantitate: Number(l.cantitate),
            observatii: l.observatii?.trim() || null,
            articolDevizId: null,
          })),
          dinUrma: !!v.dinUrma,
          noteComandaIds: v.dinUrma ? v.noteComandaIds ?? [] : [],
        },
      })
      setCreata(necesar)
      onImportat?.(`Necesarul ${necesar.numar}`)
      message.success(necesar.dinUrma
        ? `Necesarul ${necesar.numar} a fost înregistrat ca document din urmă (aprobat)${v.noteComandaIds?.length ? `, legat de ${v.noteComandaIds.length} notă(e) de comandă` : ''}.`
        : `Necesarul ${necesar.numar} a fost creat și trimis spre aprobare.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  // ------------------------------------------------------------------ după creare
  if (creata) {
    return (
      <Result
        status="success"
        title={`Necesarul ${creata.numar} a fost creat`}
        subTitle={`Număr pe document: ${creata.numarExtern ?? '—'} · ${creata.linii.length} materiale · ${creata.dinUrma ? 'document din urmă, aprobat' : 'trimis spre aprobare'}. Originalul este atașat necesarului.`}
        extra={<Button type="primary" onClick={reseteaza}>Importă alt document</Button>}
      />
    )
  }

  // ------------------------------------------------------------------ alegerea fișierului
  if (!fisier) {
    return (
      <Upload.Dragger
        accept=".pdf,.png,.jpg,.jpeg,.tif,.tiff,.docx"
        multiple={false}
        showUploadList={false}
        beforeUpload={citeste}
        disabled={!poateCrea}
        style={{ padding: '24px 0' }}
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">Trage aici necesarul de materiale sau apasă pentru a-l alege</p>
        <p className="ant-upload-hint">
          PDF scanat, poză (JPG, PNG) sau document Word. Sistemul citește documentul și îți propune datele; verifici ce nu e clar,
          apoi creezi necesarul.
        </p>
      </Upload.Dragger>
    )
  }

  // ------------------------------------------------------------------ citire în curs / verificare
  const camp = (cheie: 'numarExtern' | 'compartimentSantier') => rezultat?.[cheie]
  const numarare = { Sigur: 0, Nesigur: 0, Lipsa: 0 }
  if (rezultat) {
    for (const c of [rezultat.numarExtern, rezultat.data, rezultat.compartimentSantier]) numarare[c.certitudine]++
    for (const l of rezultat.linii) for (const c of [l.denumire, l.unitateMasura, l.cantitate]) numarare[c.certitudine]++
  }
  const esteWord = fisier.name.toLowerCase().endsWith('.docx')
  const esteImagine = /\.(png|jpe?g|tiff?|bmp)$/i.test(fisier.name)

  return (
    <Row gutter={16}>
      <Col xs={24} xl={10}>
        <Card
          size="small"
          title={
            <Space>
              {esteWord ? <FileWordOutlined /> : <ScanOutlined />}
              <Typography.Text ellipsis style={{ maxWidth: 320 }}>{fisier.name}</Typography.Text>
            </Space>
          }
          extra={<Button size="small" onClick={reseteaza}>Alt document</Button>}
          styles={{ body: { padding: 0 } }}
          style={{ position: 'sticky', top: 80 }}
        >
          {esteWord ? (
            <Empty style={{ padding: 48 }} description="Previzualizarea nu e disponibilă pentru Word; datele au fost citite direct din document." />
          ) : esteImagine ? (
            <img src={url} alt="Documentul original" style={{ width: '100%', display: 'block' }} />
          ) : (
            <iframe src={url} title="Documentul original" style={{ width: '100%', height: 760, border: 0, display: 'block' }} />
          )}
        </Card>
      </Col>

      <Col xs={24} xl={14}>
        {interpreteaza.isPending || !rezultat ? (
          <Card>
            <Flex vertical align="center" gap={12} style={{ padding: 48 }}>
              <Spin size="large" />
              <Typography.Text>{esteWord ? 'Se citește documentul…' : 'Se recunoaște textul din scanare (OCR)…'}</Typography.Text>
            </Flex>
          </Card>
        ) : (
          <Form<ValoriFormular> form={form} name="importNecesar" layout="vertical" onFinish={creeaza} requiredMark="optional">
            {rezultat.necesarExistent && (
              <Alert
                type="error"
                showIcon
                style={{ marginBottom: 16 }}
                title={`Acest document a fost deja importat ca necesarul ${rezultat.necesarExistent}.`}
              />
            )}
            <Alert
              type={numarare.Nesigur + numarare.Lipsa > 0 ? 'warning' : 'success'}
              showIcon
              style={{ marginBottom: 16 }}
              title={
                <Space wrap size={4}>
                  <span>{rezultat.dinOcr ? 'Am citit scanarea prin OCR:' : 'Am citit documentul:'}</span>
                  {(Object.keys(numarare) as Certitudine[]).map((c) => (
                    <Tag key={c} color={eticheteCertitudine[c].culoare} icon={eticheteCertitudine[c].icon}>
                      {numarare[c]} {eticheteCertitudine[c].text}
                    </Tag>
                  ))}
                </Space>
              }
              description={
                rezultat.probleme.length > 0 && (
                  <ul style={{ margin: '4px 0 0', paddingInlineStart: 18 }}>
                    {rezultat.probleme.map((p, i) => (
                      <li key={i}>{p.mesaj}</li>
                    ))}
                  </ul>
                )
              }
            />

            <Card size="small" title="Antetul necesarului" style={{ marginBottom: 16 }}>
              <Row gutter={12}>
                <Col xs={24}>
                  <Form.Item
                    name="proiectId"
                    label="Proiect / șantier"
                    rules={[{ required: true, message: 'Alege proiectul.' }]}
                    {...(rezultat.proiectPropus && valori?.proiectId === rezultat.proiectPropus.id && rezultat.proiectPropus.certitudine === 'Nesigur'
                      ? { validateStatus: 'warning' as const, help: 'Propus după textul lucrării. Verifică.' }
                      : {})}
                    extra={rezultat.lucrare.valoare && <>Lucrarea din document: „{rezultat.lucrare.valoare}”</>}
                  >
                    <Select
                      showSearch={{ optionFilterProp: 'label' }}
                      placeholder="Alege proiectul"
                      loading={proiecte.isLoading}
                      options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))}
                      onChange={() => form.setFieldValue('noteComandaIds', [])}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="numarExtern" label="Nr. pe document" {...stareCamp(camp('numarExtern'), valori?.numarExtern)}>
                    <Input maxLength={50} placeholder="ex. 81" />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item
                    name="dataDocument"
                    label="Data documentului"
                    rules={[{ required: true, message: 'Completează data.' }]}
                    {...(rezultat.data.certitudine === 'Nesigur' && valori?.dataDocument?.format('YYYY-MM-DD') === rezultat.data.valoare
                      ? { validateStatus: 'warning' as const, help: `De verificat. Citit: „${rezultat.data.textSursa ?? ''}”` }
                      : {})}
                  >
                    <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="compartimentSantier" hidden><Input /></Form.Item>
                  <Form.Item
                    name="solicitant"
                    label="Șantier"
                    rules={[{ required: true, message: 'Alege șantierul proiectului care cere materialele.' }]}
                    help={rezultat.compartimentSantier.valoare ? `Pe document: „${rezultat.compartimentSantier.valoare}”` : 'Nu apare în document; alege-l.'}
                    validateStatus={valori?.solicitant ? undefined : 'warning'}
                  >
                    <SelectSolicitant proiectId={valori?.proiectId} />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <CampDinUrma explicatie="Necesar vechi, adus din arhivă: intră direct aprobat (aprobarea s-a dat pe hârtie) și nu mai generează nota de comandă „de acceptat”, fiindcă materialele s-au comandat deja." />
                  {valori?.dinUrma && (
                    <Form.Item
                      name="noteComandaIds"
                      label="Notele de comandă ale acestui necesar, importate deja"
                      extra="Opțional: notele importate pe proiect fără necesar. Legătura apare în istoricul fiecărei note."
                    >
                      <Select
                        mode="multiple"
                        allowClear
                        showSearch={{ optionFilterProp: 'label' }}
                        disabled={!valori?.proiectId}
                        loading={note.isLoading}
                        placeholder={valori?.proiectId ? (noteFaraNecesar.length ? 'Alege notele' : 'Nicio notă importată fără necesar pe proiect') : 'Alege întâi proiectul'}
                        options={noteFaraNecesar.map((n) => ({
                          value: n.id,
                          label: `${n.numar}${n.numarExtern ? ` (nr. ${n.numarExtern})` : ''} · ${n.furnizorDenumire} · ${dayjs(n.data).format('DD.MM.YYYY')} · ${n.linii.map((l) => l.denumire).join(', ')}`,
                        }))}
                      />
                    </Form.Item>
                  )}
                </Col>
              </Row>
            </Card>

            <Card size="small" title="Materiale" style={{ marginBottom: 16 }}>
              <Row gutter={8} style={{ fontWeight: 600, color: '#4a5568', marginBottom: 4 }}>
                <Col flex="36px">Nr.</Col>
                <Col flex="auto">Denumire material</Col>
                <Col flex="110px">UM</Col>
                <Col flex="130px">Cantitate</Col>
                <Col flex="160px">Observații</Col>
                <Col flex="32px" />
              </Row>
              <Form.List name="linii">
                {(campuri, { add, remove }) => (
                  <>
                    {campuri.map(({ key, name }, index) => {
                      const linie = linii?.[name]
                      const citit = linie?.citit
                      return (
                        <Row key={key} gutter={8} align="top">
                          <Col flex="36px" style={{ paddingTop: 5 }}>
                            <Typography.Text type="secondary">{citit?.rand ?? index + 1}</Typography.Text>
                          </Col>
                          <Col flex="auto">
                            <Form.Item
                              name={[name, 'denumire']}
                              rules={[{ required: true, whitespace: true, message: 'Completează denumirea.' }]}
                              {...stareCamp(citit?.denumire, linie?.denumire)}
                            >
                              <Input maxLength={300} />
                            </Form.Item>
                          </Col>
                          <Col flex="110px">
                            <Form.Item
                              name={[name, 'unitateMasura']}
                              rules={[{ required: true, whitespace: true, message: 'UM?' }]}
                              {...stareCamp(citit?.unitateMasura, linie?.unitateMasura)}
                            >
                              <AutoComplete options={UNITATI.map((u) => ({ value: u }))} maxLength={30} />
                            </Form.Item>
                          </Col>
                          <Col flex="130px">
                            <Form.Item
                              name={[name, 'cantitate']}
                              rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, message: '> 0' }]}
                              {...stareCamp(citit?.cantitate, linie?.cantitate)}
                            >
                              <NumarRo min={0} step={1} style={{ width: '100%' }} />
                            </Form.Item>
                          </Col>
                          <Col flex="160px">
                            <Form.Item name={[name, 'observatii']}>
                              <Input maxLength={500} />
                            </Form.Item>
                          </Col>
                          <Col flex="32px">
                            <Tooltip title="Șterge rândul">
                              <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1} />
                            </Tooltip>
                          </Col>
                        </Row>
                      )
                    })}
                    <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} block>
                      Adaugă material
                    </Button>
                  </>
                )}
              </Form.List>
            </Card>

            <Flex justify="space-between" align="center" wrap gap={8}>
              <Typography.Text type="secondary">
                {valori?.dinUrma
                  ? 'Necesarul primește număr de sistem, păstrează numărul de pe document și intră aprobat (document din urmă).'
                  : 'Necesarul primește număr de sistem, păstrează numărul de pe document și intră în fluxul de aprobare.'}
              </Typography.Text>
              <Space>
                <Button onClick={reseteaza}>Renunță</Button>
                <Button type="primary" htmlType="submit" loading={importa.isPending} disabled={!poateCrea || !!rezultat.necesarExistent}>
                  Creează necesarul de materiale
                </Button>
              </Space>
            </Flex>
          </Form>
        )}
      </Col>
    </Row>
  )
}
