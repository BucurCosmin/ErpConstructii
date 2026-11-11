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
import { useNoteComanda } from '../../api/aprovizionare'
import type { CampCitit, Certitudine } from '../../api/aprovizionare'
import { useImportaAviz, useInterpreteazaAviz } from '../../api/avize'
import type { Aviz, InterpretareAviz } from '../../api/avize'
import { useProiecte } from '../../api/interogari'
import { NumarRo } from '../../componente/NumarRo'
import { CampDinUrma } from '../../componente/DocumentDinUrma'
import { eticheteCertitudine, stareCamp, UNITATI } from './campuriCitite'
import { etichetaProiect } from '../../util/format'

interface LinieFormular {
  denumire?: string
  unitateMasura?: string
  cantitate?: number | null
  citit?: { rand: number; denumire: CampCitit<string>; unitateMasura: CampCitit<string>; cantitate: CampCitit<number> }
}

/** „nou” = nota de comandă se generează din aviz; altfel Id-ul notei emise alese. */
const NOTA_NOUA = 'nou'

interface ValoriFormular {
  proiectId?: string
  numarExtern?: string
  data?: Dayjs | null
  lucrare?: string
  furnizorDenumire?: string
  furnizorCui?: string
  notaComanda?: string
  /** D-45: aviz vechi din arhivă (fără notă: nu se generează una). */
  dinUrma?: boolean
  linii: LinieFormular[]
}

/** Cheia de comparare a denumirii furnizorului („S.C. TRITON S.R.L” = „Triton SRL”). */
const cheieFirma = (s: string | undefined | null) =>
  (s ?? '').normalize('NFD').replace(/\p{M}/gu, '').toUpperCase().replace(/\b(S\.?\s?C\.?|S\.?\s?R\.?\s?L\.?|S\.?\s?A\.?)\b/g, '').replace(/[^A-Z0-9]/g, '')

/**
 * Importul unui aviz de însoțire a mărfii (D-40): marfa a venit înaintea facturii. Sistemul citește avizul (scanare, PDF,
 * Word), propune proiectul și nota de comandă pomenită pe el; la confirmare avizul se leagă de nota aleasă sau de una
 * generată din el, iar marfa apare în Stocuri → Recepții, de recepționat pe aviz. Avizul din urmă (D-45) poate rămâne fără notă.
 */
export function ImportAviz({ poateCrea, fisierInitial, onImportat, dinUrma }: {
  poateCrea: boolean
  fisierInitial?: File
  onImportat?: (descriere: string) => void
  /** Bifa „din urmă” propusă (din centrul de import). */
  dinUrma?: boolean
}) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const interpreteaza = useInterpreteazaAviz()
  const importa = useImportaAviz()
  const [form] = Form.useForm<ValoriFormular>()
  const [fisier, setFisier] = useState<File>()
  const [url, setUrl] = useState<string>()
  const [rezultat, setRezultat] = useState<InterpretareAviz>()
  const [creat, setCreat] = useState<Aviz>()
  const urlCurent = useRef<string | undefined>(undefined)

  const valori = Form.useWatch([], form) as ValoriFormular | undefined
  const linii = Form.useWatch('linii', form) as LinieFormular[] | undefined
  const proiectId = Form.useWatch('proiectId', form) as string | undefined
  const note = useNoteComanda({ proiectId, stare: 'Emisa' })
  const furnizor = cheieFirma(valori?.furnizorDenumire)
  const optiuniNote = [...(note.data ?? [])]
    .sort((a, b) => Number(cheieFirma(b.furnizorDenumire) === furnizor) - Number(cheieFirma(a.furnizorDenumire) === furnizor))

  useEffect(() => () => {
    if (urlCurent.current) URL.revokeObjectURL(urlCurent.current)
  }, [])

  const reseteaza = () => {
    if (urlCurent.current) URL.revokeObjectURL(urlCurent.current)
    urlCurent.current = undefined
    setUrl(undefined)
    setFisier(undefined)
    setRezultat(undefined)
    setCreat(undefined)
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
        proiectId: r.notaComandaPropusa?.proiectId ?? r.proiectPropus?.id,
        numarExtern: r.numarExtern.valoare ?? undefined,
        data: r.data.valoare ? dayjs(r.data.valoare) : null,
        lucrare: r.lucrare.valoare ?? undefined,
        furnizorDenumire: r.furnizorGasit?.denumire ?? r.furnizorDenumire.valoare ?? undefined,
        furnizorCui: r.furnizorGasit?.cui ?? r.furnizorCui.valoare ?? undefined,
        notaComanda: r.notaComandaPropusa?.id ?? NOTA_NOUA,
        dinUrma: !!dinUrma,
        linii: r.linii.length
          ? r.linii.map((l) => ({
              denumire: l.denumire.valoare ?? undefined,
              unitateMasura: l.unitateMasura.valoare ?? undefined,
              cantitate: l.cantitate.valoare,
              citit: { rand: l.rand, denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate },
            }))
          : [{}],
      })
    } catch (e) {
      message.error((e as Error).message)
    }
    return false
  }

  // Deschis din centrul de import: fișierul (paginile avizului) e deja ales.
  const citit = useRef<File | undefined>(undefined)
  useEffect(() => {
    if (fisierInitial && citit.current !== fisierInitial) {
      citit.current = fisierInitial
      void citeste(fisierInitial)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fisierInitial])

  const inregistreaza = async (v: ValoriFormular) => {
    if (!fisier || !v.proiectId || !v.data) return
    try {
      const aviz = await importa.mutateAsync({
        proiectId: v.proiectId,
        fisier,
        date: {
          numarExtern: (v.numarExtern ?? '').trim(),
          data: v.data.format('YYYY-MM-DD'),
          lucrare: v.lucrare?.trim() || null,
          furnizorDenumire: (v.furnizorDenumire ?? '').trim(),
          furnizorCui: v.furnizorCui?.trim() || null,
          linii: v.linii.map((l) => ({ denumire: (l.denumire ?? '').trim(), unitateMasura: (l.unitateMasura ?? '').trim(), cantitate: Number(l.cantitate) })),
          observatii: null,
          notaComandaId: !v.notaComanda || v.notaComanda === NOTA_NOUA ? null : v.notaComanda,
          dinUrma: !!v.dinUrma,
        },
      })
      setCreat(aviz)
      onImportat?.(`Avizul ${aviz.numarExtern} (${aviz.numar})`)
      message.success(`Avizul ${aviz.numarExtern} a fost înregistrat.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  if (creat) {
    return (
      <Result
        status="success"
        title={`Avizul ${creat.numarExtern} a fost înregistrat (${creat.numar})`}
        subTitle={
          creat.notaComandaNumar
            ? `${creat.furnizorDenumire} · ${creat.linii.length} materiale · nota de comandă ${creat.notaComandaNumar}` +
              `${creat.notaComandaGenerata ? ' (generată din aviz)' : ''}. Marfa apare în Stocuri → Recepții, de recepționat pe aviz; ` +
              'factura care vine se leagă de aceeași notă de comandă.'
            : `${creat.furnizorDenumire} · ${creat.linii.length} materiale · document din urmă, fără notă de comandă. Marfa apare în Stocuri → Recepții; ` +
              'factura furnizorului pe același proiect îl găsește la „Leagă de avize”.'
        }
        extra={<Button type="primary" onClick={reseteaza}>Importă alt aviz</Button>}
      />
    )
  }

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
        <p className="ant-upload-text">Trage aici avizul de însoțire a mărfii sau apasă pentru a-l alege</p>
        <p className="ant-upload-hint">
          Marfa venită înaintea facturii: PDF scanat, poză sau Word. Sistemul citește furnizorul, numărul, materialele și comanda
          pomenită pe aviz; marfa se recepționează apoi pe aviz, iar factura primește aceeași notă de comandă.
        </p>
      </Upload.Dragger>
    )
  }

  const numarare = { Sigur: 0, Nesigur: 0, Lipsa: 0 }
  if (rezultat) {
    for (const c of [rezultat.numarExtern, rezultat.data, rezultat.furnizorDenumire]) numarare[c.certitudine]++
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
          <Form<ValoriFormular> form={form} name="importAviz" layout="vertical" onFinish={inregistreaza} requiredMark="optional">
            {rezultat.avizExistent && (
              <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Acest document a fost deja importat ca ${rezultat.avizExistent}.`} />
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
                    {rezultat.probleme.map((p, i) => <li key={i}>{p.mesaj}</li>)}
                  </ul>
                )
              }
            />

            <Card size="small" title="Avizul" style={{ marginBottom: 16 }}>
              <Row gutter={12}>
                <Col xs={24} md={8}>
                  <Form.Item name="numarExtern" label="Seria și numărul" rules={[{ required: true, whitespace: true, message: 'Completează numărul avizului.' }]}
                    {...stareCamp(rezultat.numarExtern, valori?.numarExtern)}>
                    <Input maxLength={50} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={6}>
                  <Form.Item
                    name="data"
                    label="Data"
                    rules={[{ required: true, message: 'Completează data.' }]}
                    {...(rezultat.data.certitudine === 'Nesigur' && valori?.data?.format('YYYY-MM-DD') === rezultat.data.valoare
                      ? { validateStatus: 'warning' as const, help: `De verificat. Citit: „${rezultat.data.textSursa ?? ''}”` }
                      : {})}
                  >
                    <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item name="lucrare" label="Lucrare / observații (de pe aviz)">
                    <Input maxLength={500} />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Form.Item
                    name="proiectId"
                    label="Proiect"
                    rules={[{ required: true, message: 'Alege proiectul.' }]}
                    {...(rezultat.proiectPropus && valori?.proiectId === rezultat.proiectPropus.id && rezultat.proiectPropus.certitudine === 'Nesigur'
                      ? { validateStatus: 'warning' as const, help: 'Propus după textul lucrării. Verifică.' }
                      : {})}
                  >
                    <Select
                      showSearch={{ optionFilterProp: 'label' }}
                      placeholder="Alege proiectul"
                      loading={proiecte.isLoading}
                      options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))}
                      onChange={() => form.setFieldValue('notaComanda', NOTA_NOUA)}
                    />
                  </Form.Item>
                </Col>
              </Row>
            </Card>

            <Card
              size="small"
              title="Furnizor"
              extra={rezultat.furnizorGasit ? <Tag color="green">există în nomenclator</Tag> : <Tag color="blue">nou: se adaugă în nomenclator</Tag>}
              style={{ marginBottom: 16 }}
            >
              <Row gutter={12}>
                <Col xs={24} md={16}>
                  <Form.Item name="furnizorDenumire" label="Denumire" rules={[{ required: true, whitespace: true, message: 'Completează furnizorul.' }]}
                    {...stareCamp(rezultat.furnizorDenumire, valori?.furnizorDenumire)}>
                    <Input maxLength={300} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="furnizorCui" label="CUI">
                    <Input maxLength={20} disabled={!!rezultat.furnizorGasit?.cui} />
                  </Form.Item>
                </Col>
              </Row>
            </Card>

            <Card size="small" title="Nota de comandă a mărfii" style={{ marginBottom: 16 }}>
              <Form.Item
                name="notaComanda"
                extra={
                  rezultat.referintaComanda
                    ? `Pe aviz scrie comanda „${rezultat.referintaComanda}”${rezultat.notaComandaPropusa ? `: am găsit ${rezultat.notaComandaPropusa.numar}.` : ', dar n-am găsit-o printre notele emise.'}`
                    : 'Factura care vine pentru această marfă se leagă de aceeași notă de comandă.'
                }
              >
                <Select
                  showSearch={{ optionFilterProp: 'label' }}
                  loading={note.isLoading}
                  options={[
                    {
                      value: NOTA_NOUA,
                      label: valori?.dinUrma
                        ? 'Fără notă de comandă (document din urmă)'
                        : 'Generează o notă de comandă din aviz (marfă fără comandă în sistem)',
                    },
                    ...optiuniNote.map((n) => ({
                      value: n.id,
                      label: `${n.numar}${n.numarExtern ? ` (nr. ${n.numarExtern})` : ''} · ${n.furnizorDenumire} · ${dayjs(n.data).format('DD.MM.YYYY')} · ${n.linii.map((l) => l.denumire).join(', ')}`,
                    })),
                  ]}
                />
              </Form.Item>
              <CampDinUrma explicatie="Aviz vechi, adus din arhivă: fără notă de comandă aleasă, nu se mai generează una; factura lui îl găsește după furnizor și proiect." />
            </Card>

            <Card size="small" title="Materiale" style={{ marginBottom: 16 }}>
              <Row gutter={8} style={{ fontWeight: 600, color: '#4a5568', marginBottom: 4 }}>
                <Col flex="36px">Nr.</Col>
                <Col flex="auto">Denumire</Col>
                <Col flex="110px">UM</Col>
                <Col flex="150px">Cantitate</Col>
                <Col flex="32px" />
              </Row>
              <Form.List name="linii">
                {(campuri, { add, remove }) => (
                  <>
                    {campuri.map(({ key, name }, index) => {
                      const linie = linii?.[name]
                      const c = linie?.citit
                      return (
                        <Row key={key} gutter={8} align="top">
                          <Col flex="36px" style={{ paddingTop: 5 }}>
                            <Typography.Text type="secondary">{c?.rand ?? index + 1}</Typography.Text>
                          </Col>
                          <Col flex="auto">
                            <Form.Item name={[name, 'denumire']} rules={[{ required: true, whitespace: true, message: 'Completează denumirea.' }]}
                              {...stareCamp(c?.denumire, linie?.denumire)}>
                              <Input maxLength={300} />
                            </Form.Item>
                          </Col>
                          <Col flex="110px">
                            <Form.Item name={[name, 'unitateMasura']} rules={[{ required: true, whitespace: true, message: 'UM?' }]}
                              {...stareCamp(c?.unitateMasura, linie?.unitateMasura)}>
                              <AutoComplete options={UNITATI.map((u) => ({ value: u }))} maxLength={30} />
                            </Form.Item>
                          </Col>
                          <Col flex="150px">
                            <Form.Item name={[name, 'cantitate']} rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, message: '> 0' }]}
                              {...stareCamp(c?.cantitate, linie?.cantitate)}>
                              <NumarRo min={0} step={1} style={{ width: '100%' }} />
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
              <Typography.Text type="secondary">Avizul primește număr de sistem AV/AAAA/NNNN și își păstrează numărul de pe document.</Typography.Text>
              <Space>
                <Button onClick={reseteaza}>Renunță</Button>
                <Button type="primary" htmlType="submit" loading={importa.isPending} disabled={!poateCrea || !!rezultat.avizExistent}>
                  Înregistrează avizul
                </Button>
              </Space>
            </Flex>
          </Form>
        )}
      </Col>
    </Row>
  )
}
