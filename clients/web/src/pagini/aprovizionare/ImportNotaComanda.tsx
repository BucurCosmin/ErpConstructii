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
import { useImportaNotaComanda, useInterpreteazaNotaComanda, useNecesareMateriale } from '../../api/aprovizionare'
import type { CampCitit, Certitudine, InterpretareNotaComanda, NotaComanda } from '../../api/aprovizionare'
import { useProiecte } from '../../api/interogari'
import { eticheteCertitudine, stareCamp, UNITATI } from './campuriCitite'
import { NumarRo } from '../../componente/NumarRo'
import { CampDinUrma } from '../../componente/DocumentDinUrma'
import { etichetaProiect } from '../../util/format'

interface LinieFormular {
  denumire?: string
  unitateMasura?: string
  cantitate?: number | null
  termen?: string
  citit?: { rand: number; denumire: CampCitit<string>; unitateMasura: CampCitit<string>; cantitate: CampCitit<number> }
}

interface ValoriFormular {
  proiectId?: string
  numarExtern?: string
  data?: Dayjs | null
  lucrare?: string
  furnizorDenumire?: string
  furnizorCui?: string
  furnizorAdresa?: string
  furnizorTelefon?: string
  /** Necesarul din care provine comanda (opțional). */
  necesarId?: string
  /** D-45: comandă veche din arhivă. */
  dinUrma?: boolean
  linii: LinieFormular[]
}

/**
 * Importul unei note de comandă către furnizor emise pe hârtie (formularul GC 007; PDF scanat, poză, Word):
 * sistemul citește documentul, caută furnizorul în nomenclator și propune proiectul; utilizatorul verifică și înregistrează.
 * Necesarul de materiale se alege dacă există; comenzile vechi (din urmă, D-45) pot rămâne fără el și se leagă mai târziu.
 */
export function ImportNotaComanda({ poateCrea, fisierInitial, onImportat, dinUrma }: {
  poateCrea: boolean
  fisierInitial?: File
  onImportat?: (descriere: string) => void
  /** Bifa „din urmă” propusă (din centrul de import). */
  dinUrma?: boolean
}) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const interpreteaza = useInterpreteazaNotaComanda()
  const importa = useImportaNotaComanda()
  const [form] = Form.useForm<ValoriFormular>()
  const [fisier, setFisier] = useState<File>()
  const [url, setUrl] = useState<string>()
  const [rezultat, setRezultat] = useState<InterpretareNotaComanda>()
  const [creata, setCreata] = useState<NotaComanda>()
  const urlCurent = useRef<string | undefined>(undefined)

  const valori = Form.useWatch([], form) as ValoriFormular | undefined
  const linii = Form.useWatch('linii', form) as LinieFormular[] | undefined
  const necesare = useNecesareMateriale({ proiectId: valori?.proiectId }, !!valori?.proiectId)

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
        data: r.data.valoare ? dayjs(r.data.valoare) : null,
        lucrare: r.lucrare.valoare ?? undefined,
        furnizorDenumire: r.furnizorGasit?.denumire ?? r.furnizorDenumire.valoare ?? undefined,
        furnizorCui: r.furnizorGasit?.cui ?? r.furnizorCui.valoare ?? undefined,
        furnizorAdresa: r.furnizorAdresa ?? undefined,
        furnizorTelefon: r.furnizorTelefon ?? undefined,
        linii: r.linii.length
          ? r.linii.map((l) => ({
              denumire: l.denumire.valoare ?? undefined,
              unitateMasura: l.unitateMasura.valoare ?? undefined,
              cantitate: l.cantitate.valoare,
              termen: l.termen ?? undefined,
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

  const inregistreaza = async (v: ValoriFormular) => {
    if (!fisier || !v.proiectId || !v.data) return
    try {
      const nota = await importa.mutateAsync({
        proiectId: v.proiectId,
        fisier,
        date: {
          numarExtern: v.numarExtern?.trim() || null,
          data: v.data.format('YYYY-MM-DD'),
          lucrare: v.lucrare?.trim() || null,
          furnizorDenumire: (v.furnizorDenumire ?? '').trim(),
          furnizorCui: v.furnizorCui?.trim() || null,
          furnizorAdresa: v.furnizorAdresa?.trim() || null,
          furnizorTelefon: v.furnizorTelefon?.trim() || null,
          linii: v.linii.map((l) => ({
            denumire: (l.denumire ?? '').trim(),
            unitateMasura: (l.unitateMasura ?? '').trim(),
            cantitate: Number(l.cantitate),
            termen: l.termen?.trim() || null,
          })),
          observatii: null,
          necesarId: v.necesarId ?? null,
          dinUrma: !!v.dinUrma,
        },
      })
      setCreata(nota)
      onImportat?.(`Nota de comandă ${nota.numar}`)
      message.success(`Nota de comandă ${nota.numar} a fost înregistrată.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  if (creata) {
    return (
      <Result
        status="success"
        title={`Nota de comandă ${creata.numar} a fost înregistrată`}
        subTitle={`Nr. pe document: ${creata.numarExtern ?? '—'} · furnizor ${creata.furnizorDenumire} · ${creata.linii.length} materiale. Originalul este atașat.`}
        extra={<Button type="primary" onClick={reseteaza}>Importă altă notă de comandă</Button>}
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
        <p className="ant-upload-text">Trage aici nota de comandă către furnizor sau apasă pentru a o alege</p>
        <p className="ant-upload-hint">
          Formularul „COMANDA” (GC 007): PDF scanat, poză sau Word. Sistemul citește furnizorul, lucrarea și materialele;
          furnizorul se caută în nomenclator și se adaugă dacă lipsește.
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
          <Form<ValoriFormular> form={form} name="importNotaComanda" layout="vertical" onFinish={inregistreaza} requiredMark="optional">
            {rezultat.comandaExistenta && (
              <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Acest document a fost deja importat ca ${rezultat.comandaExistenta}.`} />
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

            <Card size="small" title="Antetul notei de comandă" style={{ marginBottom: 16 }}>
              <Row gutter={12}>
                <Col xs={24} md={6}>
                  <Form.Item name="numarExtern" label="Nr. pe document" {...stareCamp(rezultat.numarExtern, valori?.numarExtern)}>
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
                <Col xs={24} md={12}>
                  <Form.Item name="lucrare" label="Obiect / lucrare (de pe document)">
                    <Input maxLength={500} />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Form.Item
                    name="proiectId"
                    label="Proiect"
                    rules={[{ required: true, message: 'Alege proiectul (pentru stoc, proiectul de stoc al firmei).' }]}
                    {...(rezultat.proiectPropus && valori?.proiectId === rezultat.proiectPropus.id && rezultat.proiectPropus.certitudine === 'Nesigur'
                      ? { validateStatus: 'warning' as const, help: 'Propus după textul lucrării. Verifică.' }
                      : {})}
                  >
                    <Select
                      showSearch={{ optionFilterProp: 'label' }}
                      placeholder="Alege proiectul"
                      loading={proiecte.isLoading}
                      options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))}
                      onChange={() => form.setFieldValue('necesarId', undefined)}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Form.Item
                    name="necesarId"
                    label="Necesarul de materiale din care provine comanda"
                    extra={valori?.necesarId ? undefined : valori?.dinUrma
                      ? 'Opțional la documentele din urmă: necesarul se poate lega mai târziu, după ce îl imporți (din lista notelor sau la importul necesarului).'
                      : 'Opțional. Fără necesar, nota apare în listă ca „fără necesar”; dacă e o comandă veche, bifează „Document din urmă”.'}
                  >
                    <Select
                      allowClear
                      showSearch={{ optionFilterProp: 'label' }}
                      disabled={!valori?.proiectId}
                      loading={necesare.isLoading}
                      placeholder={valori?.proiectId ? (necesare.data?.some((n) => n.stare !== 'Respinsa') ? 'Fără necesar' : 'Proiectul nu are necesare') : 'Alege întâi proiectul'}
                      options={necesare.data?.filter((n) => n.stare !== 'Respinsa').map((n) => ({
                        value: n.id,
                        label: `${n.numar}${n.numarExtern ? ` (nr. ${n.numarExtern})` : ''} · ${dayjs(n.data).format('DD.MM.YYYY')} · ${n.compartimentSantier} · ${n.linii.map((l) => l.denumire).join(', ')}`,
                      }))}
                    />
                  </Form.Item>
                  <CampDinUrma explicatie="Comandă veche, adusă din arhivă: se înregistrează și fără necesar de materiale (neimportat încă sau pierdut)." />
                </Col>
              </Row>
            </Card>

            <Card
              size="small"
              title="Furnizor"
              extra={rezultat.furnizorGasit
                ? <Tag color="green">există în nomenclator</Tag>
                : <Tag color="blue">nou: se adaugă în nomenclator</Tag>}
              style={{ marginBottom: 16 }}
            >
              <Row gutter={12}>
                <Col xs={24} md={16}>
                  <Form.Item
                    name="furnizorDenumire"
                    label="Denumire"
                    rules={[{ required: true, whitespace: true, message: 'Completează furnizorul.' }]}
                    {...stareCamp(rezultat.furnizorDenumire, valori?.furnizorDenumire)}
                  >
                    <Input maxLength={300} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="furnizorCui" label="CUI" extra={!rezultat.furnizorGasit && 'Nu apare pe comandă; opțional.'}>
                    <Input maxLength={20} disabled={!!rezultat.furnizorGasit} />
                  </Form.Item>
                </Col>
                {!rezultat.furnizorGasit && (
                  <>
                    <Col xs={24} md={16}>
                      <Form.Item name="furnizorAdresa" label="Adresa">
                        <Input maxLength={300} />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={8}>
                      <Form.Item name="furnizorTelefon" label="Telefon">
                        <Input maxLength={60} />
                      </Form.Item>
                    </Col>
                  </>
                )}
              </Row>
            </Card>

            <Card size="small" title="Materiale" style={{ marginBottom: 16 }}>
              <Row gutter={8} style={{ fontWeight: 600, color: '#4a5568', marginBottom: 4 }}>
                <Col flex="36px">Nr.</Col>
                <Col flex="auto">Material / produs</Col>
                <Col flex="110px">UM</Col>
                <Col flex="130px">Cantitate</Col>
                <Col flex="140px">Termen</Col>
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
                            <Form.Item name={[name, 'denumire']} rules={[{ required: true, whitespace: true, message: 'Completează denumirea.' }]}
                              {...stareCamp(citit?.denumire, linie?.denumire)}>
                              <Input maxLength={300} />
                            </Form.Item>
                          </Col>
                          <Col flex="110px">
                            <Form.Item name={[name, 'unitateMasura']} rules={[{ required: true, whitespace: true, message: 'UM?' }]}
                              {...stareCamp(citit?.unitateMasura, linie?.unitateMasura)}>
                              <AutoComplete options={UNITATI.map((u) => ({ value: u }))} maxLength={30} />
                            </Form.Item>
                          </Col>
                          <Col flex="130px">
                            <Form.Item name={[name, 'cantitate']}
                              rules={[{ required: true, message: 'Cantitatea?' }, { type: 'number', min: 0.0001, message: '> 0' }]}
                              {...stareCamp(citit?.cantitate, linie?.cantitate)}>
                              <NumarRo min={0} step={1} style={{ width: '100%' }} />
                            </Form.Item>
                          </Col>
                          <Col flex="140px">
                            <Form.Item name={[name, 'termen']}>
                              <Input maxLength={100} />
                            </Form.Item>
                          </Col>
                          <Col flex="32px">
                            <Tooltip title="Șterge rândul (ex. tăiat pe hârtie)">
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
                Nota primește număr de sistem NC/AAAA/NNNN și își păstrează numărul de pe document.
              </Typography.Text>
              <Space>
                <Button onClick={reseteaza}>Renunță</Button>
                <Button type="primary" htmlType="submit" loading={importa.isPending} disabled={!poateCrea || !!rezultat.comandaExistenta}>
                  Înregistrează nota de comandă
                </Button>
              </Space>
            </Flex>
          </Form>
        )}
      </Col>
    </Row>
  )
}
