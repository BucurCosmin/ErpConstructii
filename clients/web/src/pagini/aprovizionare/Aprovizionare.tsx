import { CheckOutlined, CloseOutlined, DownloadOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Form, Input, Modal, Row, Select, Space, Table, Tabs, Tag, Typography, Upload } from 'antd'
import type { UploadFile } from 'antd'
import { useEffect, useState } from 'react'
import dayjs from 'dayjs'
import { api } from '../../api/client'
import {
  useAprobaNotaComanda,
  useEmiteNotaComanda,
  useNoteComanda,
  useRespingeNotaComanda,
} from '../../api/aprovizionare'
import type { FiltreNoteComanda, NotaComanda } from '../../api/aprovizionare'
import {
  useImportaFormular,
  useImporturiFormular,
  useSabloaneFormular,
} from '../../api/formulare'
import type { DateFormularApi, ImportFormularApi } from '../../api/formulare'
import { usePermisiuni, useProfil, useProiecte } from '../../api/interogari'
import type { Proiect } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'

interface LinieNotaValues {
  denumire: string
  unitateMasura: string
  cantitate: string
  observatii?: string
}

interface NotaValues {
  compartimentSantier: string
  linii: LinieNotaValues[]
}

interface LinieImport {
  cod_articol?: string
  denumire?: string
  um?: string
  cantitate?: string
  pret_unitar?: string
  valoare?: string
  articol_deviz?: string
  termen?: string
}

interface ImportValues {
  numar: string
  data: string
  proiect_cod: string
  proiect_denumire?: string
  furnizor_denumire?: string
  furnizor_cui?: string
  data_scadenta?: string
  total_cu_tva?: string
  total_fara_tva?: string
  observatii?: string
  linii?: LinieImport[]
}

interface RespingereValues {
  motiv: string
}

interface FiltreNotaValues {
  proiectId?: string
  lucrare?: string
  material?: string
  dataDe?: string
  dataPanaLa?: string
  stare?: NotaComanda['stare']
  numar?: string
  intocmit?: string
  aprobatDe?: string
}

function LiniiNota() {
  return (
    <Form.List name="linii">
      {(fields, { add, remove }) => (
        <Space direction="vertical" style={{ width: '100%' }} size="small">
          {fields.map(({ key, name }) => (
            <Card key={key} size="small" title={`Material ${name + 1}`} extra={
              <Button type="link" danger onClick={() => remove(name)}>Șterge</Button>
            }>
              <Row gutter={12}>
                <Col xs={24} md={10}>
                  <Form.Item name={[name, 'denumire']} label="Denumire material" rules={[{ required: true }]}>
                    <Input maxLength={300} />
                  </Form.Item>
                </Col>
                <Col xs={12} md={4}>
                  <Form.Item name={[name, 'unitateMasura']} label="UM" rules={[{ required: true }]}>
                    <Input maxLength={30} />
                  </Form.Item>
                </Col>
                <Col xs={12} md={4}>
                  <Form.Item name={[name, 'cantitate']} label="Cantitate" rules={[{ required: true }]}>
                    <Input type="number" min={0.0001} step={0.0001} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={6}>
                  <Form.Item name={[name, 'observatii']} label="Observații">
                    <Input maxLength={500} />
                  </Form.Item>
                </Col>
              </Row>
            </Card>
          ))}
          <Button icon={<PlusOutlined />} onClick={() => add({})}>Adaugă material</Button>
        </Space>
      )}
    </Form.List>
  )
}

function LiniiImport({ obligatoriu }: { obligatoriu: boolean }) {
  return (
    <Form.List name="linii">
      {(fields, { add, remove }) => (
        <Space direction="vertical" style={{ width: '100%' }} size="small">
          {fields.map(({ key, name }) => (
            <Card key={key} size="small" title={`Poziția ${name + 1}`} extra={
              <Button type="link" danger onClick={() => remove(name)}>Șterge</Button>
            }>
              <Row gutter={12}>
                <Col xs={24} md={8}><Form.Item name={[name, 'denumire']} label="Denumire" rules={obligatoriu ? [{ required: true }] : []}><Input /></Form.Item></Col>
                <Col xs={12} md={4}><Form.Item name={[name, 'um']} label="UM" rules={obligatoriu ? [{ required: true }] : []}><Input /></Form.Item></Col>
                <Col xs={12} md={4}><Form.Item name={[name, 'cantitate']} label="Cantitate" rules={obligatoriu ? [{ required: true }] : []}><Input /></Form.Item></Col>
                <Col xs={12} md={4}><Form.Item name={[name, 'cod_articol']} label="Cod articol"><Input /></Form.Item></Col>
                <Col xs={12} md={4}><Form.Item name={[name, 'articol_deviz']} label="Articol deviz"><Input /></Form.Item></Col>
              </Row>
            </Card>
          ))}
          <Button icon={<PlusOutlined />} onClick={() => add({})}>Adaugă poziție</Button>
        </Space>
      )}
    </Form.List>
  )
}

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = nume
  link.click()
  URL.revokeObjectURL(url)
}

function etichetaStare(stare: NotaComanda['stare']) {
  switch (stare) {
    case 'InAsteptareAprobare':
      return <Tag color="gold">În așteptare aprobare</Tag>
    case 'Aprobata':
      return <Tag color="green">Aprobată</Tag>
    case 'Respinsa':
      return <Tag color="red">Respinsă</Tag>
  }
}

export function Aprovizionare() {
  const { message } = App.useApp()
  const permisiuni = usePermisiuni()
  const profil = useProfil()
  const proiecte = useProiecte()
  const sabloane = useSabloaneFormular()
  const importuri = useImporturiFormular()
  const emiteNota = useEmiteNotaComanda()
  const aprobaNota = useAprobaNotaComanda()
  const respingeNota = useRespingeNotaComanda()
  const importa = useImportaFormular()
  const [formNota] = Form.useForm<NotaValues>()
  const [formImport] = Form.useForm<ImportValues>()
  const [formRespingere] = Form.useForm<RespingereValues>()
  const [formFiltre] = Form.useForm<FiltreNotaValues>()
  const [proiectId, setProiectId] = useState('')
  const [filtreNote, setFiltreNote] = useState<FiltreNoteComanda>({})
  const [notaSelectata, setNotaSelectata] = useState<NotaComanda>()
  const [fisierImport, setFisierImport] = useState<File>()
  const [tipImport, setTipImport] = useState<'comanda-furnizor' | 'factura-primita'>('factura-primita')

  const note = useNoteComanda(filtreNote)
  const importuriFormular = useImporturiFormular()
  const proiect = proiecte.data?.find((p: Proiect) => p.id === proiectId)
  const sablonActiv = sabloane.data?.some((sablon) => sablon.codTip === 'comanda-furnizor' && sablon.activ) ?? false
  const poateCrea = permisiuni.data?.includes('aprovizionare.creare') ?? false
  const poateAproba = permisiuni.data?.includes('aprovizionare.aprobare') ?? false

  const aplicaFiltreNote = (valori: FiltreNotaValues) => {
    setFiltreNote({
      proiectId: valori.proiectId || undefined,
      lucrare: valori.lucrare?.trim() || undefined,
      material: valori.material?.trim() || undefined,
      dataDe: valori.dataDe || undefined,
      dataPanaLa: valori.dataPanaLa || undefined,
      stare: valori.stare || undefined,
      numar: valori.numar?.trim() || undefined,
      intocmit: valori.intocmit?.trim() || undefined,
      aprobatDe: valori.aprobatDe?.trim() || undefined,
    })
  }

  useEffect(() => {
    if (!proiectId && proiecte.data?.length) setProiectId(proiecte.data[0].id)
  }, [proiectId, proiecte.data])

  const emite = async (valori: NotaValues) => {
    if (!proiectId) {
      message.warning('Selectează proiectul / șantierul.')
      return
    }

    try {
      const nota = await emiteNota.mutateAsync({
        proiectId,
        compartimentSantier: valori.compartimentSantier.trim(),
        linii: valori.linii.map((linie) => ({
          denumire: linie.denumire.trim(),
          unitateMasura: linie.unitateMasura.trim(),
          cantitate: Number(linie.cantitate),
          observatii: linie.observatii?.trim() || null,
          articolDevizId: null,
        })),
      })
      formNota.resetFields()
      message.success(`Nota ${nota.numar} a fost emisă și trimisă spre aprobare.`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Nota nu a putut fi emisă.')
    }
  }

  const aproba = async (nota: NotaComanda) => {
    try {
      await aprobaNota.mutateAsync({ proiectId: nota.proiectId, id: nota.id })
      message.success(`Nota ${nota.numar} a fost aprobată.`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Nota nu a putut fi aprobată.')
    }
  }

  const respinge = async (valori: RespingereValues) => {
    if (!notaSelectata) return
    try {
      await respingeNota.mutateAsync({ proiectId: notaSelectata.proiectId, id: notaSelectata.id, motiv: valori.motiv.trim() })
      message.success(`Nota ${notaSelectata.numar} a fost respinsă.`)
      setNotaSelectata(undefined)
      formRespingere.resetFields()
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Nota nu a putut fi respinsă.')
    }
  }

  const descarcaNota = async (nota: NotaComanda) => {
    try {
      const document = await api.getBinar(`/api/aprovizionare/proiecte/${nota.proiectId}/note-comanda/${nota.id}/document.docx`)
      descarca(document, `${nota.numar.replace(/[^a-zA-Z0-9_-]/g, '_')}.docx`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Documentul nu a putut fi generat.')
    }
  }

  const importaDocument = async (valori: ImportValues) => {
    if (!fisierImport) {
      message.warning('Alege documentul original PDF sau DOCX.')
      return
    }

    const esteFactura = tipImport === 'factura-primita'
    const date: DateFormularApi = {
      campuri: {
        numar: valori.numar,
        data: valori.data,
        proiect_cod: valori.proiect_cod,
        proiect_denumire: valori.proiect_denumire ?? '',
        furnizor_denumire: esteFactura ? valori.furnizor_denumire ?? '' : '',
        furnizor_cui: esteFactura ? valori.furnizor_cui ?? '' : '',
        data_scadenta: esteFactura ? valori.data_scadenta ?? '' : '',
        total_cu_tva: esteFactura ? valori.total_cu_tva ?? '' : '',
        total_fara_tva: valori.total_fara_tva ?? '',
        observatii: valori.observatii ?? '',
      },
      tabele: {
        linii: (valori.linii ?? []).map((linie) => ({
          cod_articol: linie.cod_articol ?? '',
          denumire: linie.denumire ?? '',
          um: linie.um ?? '',
          cantitate: linie.cantitate ?? '',
          pret_unitar: linie.pret_unitar ?? '',
          valoare: linie.valoare ?? '',
          articol_deviz: linie.articol_deviz ?? '',
          termen: linie.termen ?? '',
        })),
      },
    }

    try {
      await importa.mutateAsync({ codTip: tipImport, fisier: fisierImport, date })
      message.success('Documentul original a fost arhivat ca ciornă „De verificat”.')
      setFisierImport(undefined)
      formImport.resetFields()
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Documentul nu a putut fi importat.')
    }
  }

  const descarcaOriginal = async (document: ImportFormularApi) => {
    try {
      const original = await api.getBinar(`/api/formulare/import/${document.id}/original`)
      descarca(original, document.numeFisier)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Originalul nu a putut fi descărcat.')
    }
  }

  const schimbaTipImport = (tip: 'comanda-furnizor' | 'factura-primita') => {
    setTipImport(tip)
    formImport.resetFields()
  }

  const randuriNote = note.data ?? []

  return (
    <>
      <AntetPagina
        titlu="Aprovizionare"
        subtitlu="Emite, filtrează și arhivează note de comandă pentru proiecte; importă documente externe."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Aprovizionare' }]}
      />

      <Tabs
        items={[
          {
            key: 'emise',
            label: 'Note comandă emise',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                <Card title="Note comandă emise">
                  {note.error && <Alert type="error" showIcon message="Notele de comandă nu au putut fi încărcate." description={note.error.message} />}
                  <Form form={formFiltre} layout="vertical" onFinish={aplicaFiltreNote}>
                    <Row gutter={12}>
                      <Col xs={24} md={8}>
                        <Form.Item name="proiectId" label="Lucrare / proiect">
                          <Select
                            allowClear
                            showSearch
                            optionFilterProp="label"
                            placeholder="Toate lucrările"
                            options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))}
                          />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={8}>
                        <Form.Item name="lucrare" label="Caută în lucrare">
                          <Input maxLength={300} placeholder="Denumirea lucrării" />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={8}>
                        <Form.Item name="material" label="Material">
                          <Input maxLength={300} placeholder="Denumirea materialului" />
                        </Form.Item>
                      </Col>
                      <Col xs={12} md={4}>
                        <Form.Item name="dataDe" label="Data de la">
                          <Input type="date" />
                        </Form.Item>
                      </Col>
                      <Col xs={12} md={4}>
                        <Form.Item name="dataPanaLa" label="Data până la">
                          <Input type="date" />
                        </Form.Item>
                      </Col>
                      <Col xs={12} md={4}>
                        <Form.Item name="stare" label="Status">
                          <Select
                            allowClear
                            placeholder="Toate"
                            options={[
                              { value: 'InAsteptareAprobare', label: 'În așteptare aprobare' },
                              { value: 'Aprobata', label: 'Aprobată' },
                              { value: 'Respinsa', label: 'Respinsă' },
                            ]}
                          />
                        </Form.Item>
                      </Col>
                      <Col xs={12} md={4}>
                        <Form.Item name="numar" label="Număr">
                          <Input maxLength={30} placeholder="NC/AAAA/NNNN" />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={8}>
                        <Form.Item name="intocmit" label="Întocmit de">
                          <Input maxLength={200} placeholder="Numele emitentului" />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={8}>
                        <Form.Item name="aprobatDe" label="Aprobat de">
                          <Input maxLength={200} placeholder="Numele aprobatorului" />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={8}>
                        <Form.Item label=" ">
                          <Space>
                            <Button type="primary" htmlType="submit">Filtrează</Button>
                            <Button onClick={() => {
                              formFiltre.resetFields()
                              setFiltreNote({})
                            }}>
                              Resetează
                            </Button>
                          </Space>
                        </Form.Item>
                      </Col>
                    </Row>
                  </Form>
                  <Table<NotaComanda>
                    rowKey="id"
                    loading={note.isLoading}
                    dataSource={randuriNote}
                    tableLayout="fixed"
                    expandable={{
                      expandedRowRender: (nota) => (
                        <div>
                          <Row gutter={[12, 8]} style={{ marginBottom: 12 }}>
                            <Col xs={24} md={8}><Typography.Text><strong>Proiect:</strong> {nota.proiectCod}</Typography.Text></Col>
                            <Col xs={24} md={8}><Typography.Text><strong>Întocmit de:</strong> {nota.emitentNume}</Typography.Text></Col>
                            {nota.decisaDeNume && (
                              <Col xs={24} md={8}>
                                <Typography.Text>
                                  <strong>{nota.stare === 'Aprobata' ? 'Aprobată de:' : 'Respinsă de:'}</strong> {nota.decisaDeNume}
                                  {nota.decisaLa && ` — ${new Date(nota.decisaLa).toLocaleString('ro-RO')}`}
                                </Typography.Text>
                              </Col>
                            )}
                          </Row>
                          <Table<NotaComanda['linii'][number]>
                            rowKey={(linie) => `${linie.denumire}-${linie.unitateMasura}`}
                            dataSource={nota.linii}
                            pagination={false}
                            size="small"
                            columns={[
                              { title: 'Material', dataIndex: 'denumire' },
                              { title: 'UM', dataIndex: 'unitateMasura', width: 90 },
                              { title: 'Cantitate', dataIndex: 'cantitate', width: 120 },
                              { title: 'Observații', dataIndex: 'observatii' },
                            ]}
                          />
                          {nota.motivRespingere && <Alert type="error" message={`Motivul respingerii: ${nota.motivRespingere}`} style={{ marginTop: 12 }} />}
                        </div>
                      ),
                    }}
                    pagination={{ pageSize: 10, hideOnSinglePage: true }}
                    columns={[
                      { title: 'Număr', dataIndex: 'numar', width: 140 },
                      { title: 'Data', dataIndex: 'data', width: 110, responsive: ['sm'], render: (data: string) => new Date(`${data}T00:00:00`).toLocaleDateString('ro-RO') },
                      { title: 'Lucrare', dataIndex: 'lucrare', width: 260, responsive: ['md'], ellipsis: true },
                      { title: 'Status', dataIndex: 'stare', width: 180, render: (stare: NotaComanda['stare']) => etichetaStare(stare) },
                      {
                        title: 'Acțiuni',
                        key: 'actiuni',
                        width: 240,
                        render: (_, nota: NotaComanda) => (
                          <Space wrap>
                            {poateAproba && nota.stare === 'InAsteptareAprobare' && nota.emitentId !== profil.data?.id && (
                              <>
                                <Button size="small" type="primary" icon={<CheckOutlined />} loading={aprobaNota.isPending} onClick={() => void aproba(nota)}>
                                  Aprobă
                                </Button>
                                <Button size="small" danger icon={<CloseOutlined />} onClick={() => setNotaSelectata(nota)}>
                                  Respinge
                                </Button>
                              </>
                            )}
                            {poateAproba && nota.stare === 'InAsteptareAprobare' && nota.emitentId === profil.data?.id && (
                              <Tag>Emitentul nu poate aproba</Tag>
                            )}
                            <Button size="small" icon={<DownloadOutlined />} onClick={() => void descarcaNota(nota)}>
                              DOCX
                            </Button>
                          </Space>
                        ),
                      },
                    ]}
                  />
                </Card>
              </Space>
            ),
          },
          {
            key: 'noua',
            label: 'Notă de comandă',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                {!sablonActiv && (
                  <Alert
                    type="warning"
                    showIcon
                    message="Nu există un șablon activ pentru nota de comandă."
                    description="Încarcă documentul „Necesar materiale” din Formulare și documente înainte de emitere."
                  />
                )}
                <Card style={{ marginBottom: 16 }}>
                  <Row gutter={16} align="middle">
                    <Col xs={24} md={12}>
                      <Typography.Text strong>Proiect / șantier</Typography.Text>
                      <Select
                        style={{ display: 'block', marginTop: 6 }}
                        value={proiectId || undefined}
                        loading={proiecte.isLoading}
                        options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))}
                        onChange={setProiectId}
                        showSearch
                        optionFilterProp="label"
                        placeholder="Selectează proiectul"
                      />
                    </Col>
                    {proiect && (
                      <Col xs={24} md={12}>
                        <Typography.Text strong>Lucrare completată automat: </Typography.Text>
                        <Typography.Text>{proiect.denumire}</Typography.Text>
                      </Col>
                    )}
                  </Row>
                </Card>
                <Card title="Notă nouă de comandă">
                  <Alert
                    type="info"
                    showIcon
                    message="Numărul și data se generează automat la emitere."
                    description="Lucrarea este preluată din proiect, iar compartimentul / șantierul se completează manual. Nota nu are furnizor și intră în lista de așteptare pentru aprobare."
                    style={{ marginBottom: 16 }}
                  />
                  <Form form={formNota} layout="vertical" initialValues={{ linii: [{}] }} onFinish={emite}>
                    <Form.Item
                      name="compartimentSantier"
                      label="Compartiment / Șantier"
                      rules={[{ required: true, whitespace: true, message: 'Completează compartimentul / șantierul.' }]}
                    >
                      <Input maxLength={300} />
                    </Form.Item>
                    <LiniiNota />
                    <Form.Item style={{ marginTop: 16, marginBottom: 0 }}>
                      <Button type="primary" htmlType="submit" loading={emiteNota.isPending} disabled={!poateCrea || !proiectId || !sablonActiv}>
                        Emite și trimite spre aprobare
                      </Button>
                    </Form.Item>
                  </Form>
                </Card>
              </Space>
            ),
          },
          {
            key: 'import',
            label: 'Import document extern',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                <Card title="Import de notă de comandă sau factură primită">
                  <Alert
                    type="info"
                    showIcon
                    message="Originalul este păstrat nemodificat, iar datele introduse intră ca ciornă „De verificat”."
                    description="La importul unei note de comandă, furnizorul nu este solicitat. Facturile primite păstrează datele furnizorului."
                    style={{ marginBottom: 16 }}
                  />
                  <Form form={formImport} layout="vertical" initialValues={{ data: dayjs().format('YYYY-MM-DD'), linii: [{}] }} onFinish={importaDocument}>
                    <Row gutter={16}>
                      <Col xs={24} md={8}>
                        <Form.Item label="Tip document">
                          <Select
                            value={tipImport}
                            onChange={schimbaTipImport}
                            options={[
                              { value: 'comanda-furnizor', label: 'Notă de comandă' },
                              { value: 'factura-primita', label: 'Factură primită' },
                            ]}
                          />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={16}>
                        <Form.Item label="Original PDF sau DOCX" required>
                          <Upload
                            accept=".pdf,.docx"
                            maxCount={1}
                            fileList={fisierImport ? ([{ uid: fisierImport.name, name: fisierImport.name, status: 'done' }] as UploadFile[]) : []}
                            beforeUpload={(fisier) => {
                              setFisierImport(fisier)
                              return false
                            }}
                            onRemove={() => {
                              setFisierImport(undefined)
                              return true
                            }}
                          >
                            <Button>Adaugă originalul</Button>
                          </Upload>
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={6}><Form.Item name="numar" label="Număr document" rules={[{ required: true }]}><Input /></Form.Item></Col>
                      <Col xs={24} md={6}><Form.Item name="data" label="Data documentului" rules={[{ required: true }]}><Input type="date" /></Form.Item></Col>
                      <Col xs={24} md={6}><Form.Item name="proiect_cod" label="Cod proiect" rules={[{ required: true }]}><Input /></Form.Item></Col>
                      <Col xs={24} md={6}><Form.Item name="proiect_denumire" label="Proiect"><Input /></Form.Item></Col>
                      {tipImport === 'factura-primita' && (
                        <>
                          <Col xs={24} md={8}><Form.Item name="furnizor_denumire" label="Furnizor" rules={[{ required: true }]}><Input /></Form.Item></Col>
                          <Col xs={24} md={8}><Form.Item name="furnizor_cui" label="CUI furnizor"><Input /></Form.Item></Col>
                          <Col xs={24} md={8}><Form.Item name="data_scadenta" label="Data scadentă"><Input type="date" /></Form.Item></Col>
                          <Col xs={24} md={8}><Form.Item name="total_cu_tva" label="Total de plată" rules={[{ required: true }]}><Input /></Form.Item></Col>
                          <Col xs={24} md={8}><Form.Item name="total_fara_tva" label="Total fără TVA"><Input /></Form.Item></Col>
                        </>
                      )}
                      {tipImport === 'comanda-furnizor' && (
                        <Col span={24}><LiniiImport obligatoriu /></Col>
                      )}
                      <Col span={24}><Form.Item name="observatii" label="Observații"><Input.TextArea rows={2} /></Form.Item></Col>
                    </Row>
                    <Button type="primary" htmlType="submit" loading={importa.isPending} disabled={!poateCrea || !fisierImport}>
                      Importă ca ciornă de verificat
                    </Button>
                  </Form>
                </Card>

                <Card title="Documente importate — de verificat">
                  {importuriFormular.error && <Alert type="error" showIcon message="Documentele importate nu au putut fi încărcate." description={importuriFormular.error.message} />}
                  <Table<ImportFormularApi>
                    rowKey="id"
                    loading={importuriFormular.isLoading}
                    dataSource={importuriFormular.data}
                    expandable={{
                      expandedRowRender: (document) => (
                        <pre style={{ whiteSpace: 'pre-wrap', margin: 0 }}>
                          {JSON.stringify(document.date, null, 2)}
                        </pre>
                      ),
                    }}
                    pagination={{ pageSize: 10, hideOnSinglePage: true }}
                    columns={[
                      { title: 'Tip', dataIndex: 'codTip', render: (cod: string) => cod === 'factura-primita' ? 'Factură primită' : 'Notă de comandă' },
                      { title: 'Număr fișier', dataIndex: 'numeFisier', ellipsis: true },
                      { title: 'SHA-256', dataIndex: 'hashSha256', ellipsis: true },
                      { title: 'Stare', dataIndex: 'stare', render: (stare: string) => <Tag color="gold">{stare}</Tag> },
                      { title: 'Importat la', dataIndex: 'creatLa', render: (data: string) => new Date(data).toLocaleString('ro-RO') },
                      {
                        title: 'Original',
                        key: 'original',
                        render: (_, document) => <Button type="link" icon={<DownloadOutlined />} onClick={() => void descarcaOriginal(document)}>Descarcă</Button>,
                      },
                    ]}
                  />
                </Card>
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={`Respinge nota ${notaSelectata?.numar ?? ''}`}
        open={Boolean(notaSelectata)}
        onCancel={() => {
          setNotaSelectata(undefined)
          formRespingere.resetFields()
        }}
        onOk={() => void formRespingere.submit()}
        confirmLoading={respingeNota.isPending}
        okText="Respinge nota"
        cancelText="Anulează"
        okButtonProps={{ danger: true }}
      >
        <Form form={formRespingere} layout="vertical" onFinish={respinge}>
          <Form.Item name="motiv" label="Motivul respingerii" rules={[{ required: true, whitespace: true, message: 'Motivul este obligatoriu.' }]}>
            <Input.TextArea rows={4} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
