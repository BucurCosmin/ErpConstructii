import { CheckOutlined, CloseOutlined, DownloadOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Form, Input, Modal, Row, Select, Space, Table, Tabs, Tag, Tooltip, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import {
  useAprobaNecesarMateriale,
  useEmiteNecesarMateriale,
  useNecesareMateriale,
  useRespingeNecesarMateriale,
} from '../../api/aprovizionare'
import type { FiltreNecesareMateriale, NecesarMateriale } from '../../api/aprovizionare'
import {
  useImporturiFormular,
  useSabloaneFormular,
} from '../../api/formulare'
import type { ImportFormularApi } from '../../api/formulare'
import { usePermisiuni, useProfil, useProiecte } from '../../api/interogari'
import type { Proiect } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { ImportNecesarExtern } from './ImportNecesarExtern'
import { ImportNotaComanda } from './ImportNotaComanda'
import { ListaNoteComanda } from './ListaNoteComanda'
import { NotaComandaNoua } from './NotaComandaNoua'

interface LinieNecesarValues {
  denumire: string
  unitateMasura: string
  cantitate: string
  observatii?: string
}

interface NecesarValues {
  compartimentSantier: string
  linii: LinieNecesarValues[]
}

interface RespingereValues {
  motiv: string
}

interface FiltreNecesarValues {
  proiectId?: string
  lucrare?: string
  material?: string
  dataDe?: string
  dataPanaLa?: string
  stare?: NecesarMateriale['stare']
  numar?: string
  intocmit?: string
  aprobatDe?: string
}

function LiniiNecesar() {
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

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = nume
  link.click()
  URL.revokeObjectURL(url)
}

function etichetaStare(stare: NecesarMateriale['stare']) {
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
  const navigheaza = useNavigate()
  const permisiuni = usePermisiuni()
  const profil = useProfil()
  const proiecte = useProiecte()
  const sabloane = useSabloaneFormular()
  const emiteNota = useEmiteNecesarMateriale()
  const aprobaNota = useAprobaNecesarMateriale()
  const respingeNota = useRespingeNecesarMateriale()
  const [formNota] = Form.useForm<NecesarValues>()
  const [formRespingere] = Form.useForm<RespingereValues>()
  const [formFiltre] = Form.useForm<FiltreNecesarValues>()
  const [proiectId, setProiectId] = useState('')
  const [filtreNote, setFiltreNote] = useState<FiltreNecesareMateriale>({})
  const [notaSelectata, setNotaSelectata] = useState<NecesarMateriale>()
  const [tipImport, setTipImport] = useState<'necesar-materiale' | 'nota-comanda' | 'factura-primita'>('necesar-materiale')

  const note = useNecesareMateriale(filtreNote)
  const importuriFormular = useImporturiFormular()
  const proiect = proiecte.data?.find((p: Proiect) => p.id === proiectId)
  const sablonActiv = sabloane.data?.some((sablon) => sablon.codTip === 'necesar-materiale' && sablon.activ) ?? false
  const poateCrea = permisiuni.data?.includes('aprovizionare.creare') ?? false
  const [tab, setTab] = useState('necesare')
  const poateAproba = permisiuni.data?.includes('aprovizionare.aprobare') ?? false

  const aplicaFiltreNote = (valori: FiltreNecesarValues) => {
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

  const emite = async (valori: NecesarValues) => {
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
      message.success(`Necesarul ${nota.numar} a fost emis și trimis spre aprobare.`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Necesarul nu a putut fi emis.')
    }
  }

  const aproba = async (nota: NecesarMateriale) => {
    try {
      await aprobaNota.mutateAsync({ proiectId: nota.proiectId, id: nota.id })
      message.success(`Necesarul ${nota.numar} a fost aprobat.`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Necesarul nu a putut fi aprobat.')
    }
  }

  const respinge = async (valori: RespingereValues) => {
    if (!notaSelectata) return
    try {
      await respingeNota.mutateAsync({ proiectId: notaSelectata.proiectId, id: notaSelectata.id, motiv: valori.motiv.trim() })
      message.success(`Necesarul ${notaSelectata.numar} a fost respins.`)
      setNotaSelectata(undefined)
      formRespingere.resetFields()
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Necesarul nu a putut fi respins.')
    }
  }

  const descarcaNota = async (nota: NecesarMateriale) => {
    try {
      const document = await api.getBinar(`/api/aprovizionare/proiecte/${nota.proiectId}/necesare-materiale/${nota.id}/document.docx`)
      descarca(document, `${nota.numar.replace(/[^a-zA-Z0-9_-]/g, '_')}.docx`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Documentul nu a putut fi generat.')
    }
  }

  const descarcaOriginalNota = async (nota: NecesarMateriale) => {
    try {
      const original = await api.getBinar(`/api/aprovizionare/proiecte/${nota.proiectId}/necesare-materiale/${nota.id}/original`)
      descarca(original, `original-${nota.numar.replace(/[^a-zA-Z0-9_-]/g, '_')}`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Originalul nu a putut fi descărcat.')
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


  const randuriNote = note.data ?? []

  return (
    <>
      <AntetPagina
        titlu="Aprovizionare"
        subtitlu="Necesare de materiale (cererile șantierelor, cu aprobare), note de comandă către furnizori și importul documentelor emise pe hârtie."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Aprovizionare' }]}
      />

      <Tabs
        activeKey={tab}
        onChange={setTab}
        items={[
          {
            key: 'necesare',
            label: 'Necesare de materiale',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                <Card title="Necesare de materiale">
                  {note.error && <Alert type="error" showIcon message="Necesarele de materiale nu au putut fi încărcate." description={note.error.message} />}
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
                          <Input maxLength={30} placeholder="NM/AAAA/NNNN" />
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
                  <Table<NecesarMateriale>
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
                          <Table<NecesarMateriale['linii'][number]>
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
                      {
                        title: 'Număr',
                        dataIndex: 'numar',
                        width: 170,
                        render: (numar: string, nota: NecesarMateriale) => (
                          <Space orientation="vertical" size={0}>
                            <span>{numar}</span>
                            {nota.sursa === 'Import' && (
                              <Tooltip title="Necesar emis în afara sistemului; numărul de pe documentul original">
                                <Tag color="purple" style={{ marginInlineEnd: 0 }}>importat{nota.numarExtern ? ` · nr. ${nota.numarExtern}` : ''}</Tag>
                              </Tooltip>
                            )}
                          </Space>
                        ),
                      },
                      { title: 'Data', dataIndex: 'data', width: 110, responsive: ['sm'], render: (data: string) => new Date(`${data}T00:00:00`).toLocaleDateString('ro-RO') },
                      { title: 'Lucrare', dataIndex: 'lucrare', width: 260, responsive: ['md'], ellipsis: true },
                      { title: 'Status', dataIndex: 'stare', width: 180, render: (stare: NecesarMateriale['stare']) => etichetaStare(stare) },
                      {
                        title: 'Acțiuni',
                        key: 'actiuni',
                        width: 240,
                        render: (_, nota: NecesarMateriale) => (
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
                            {nota.versiuneSablon > 0 && (
                              <Button size="small" icon={<DownloadOutlined />} onClick={() => void descarcaNota(nota)}>
                                DOCX
                              </Button>
                            )}
                            {nota.areOriginal && (
                              <Button size="small" icon={<DownloadOutlined />} onClick={() => void descarcaOriginalNota(nota)}>
                                Original
                              </Button>
                            )}
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
            key: 'necesar-nou',
            label: 'Necesar nou',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                {!sablonActiv && (
                  <Alert
                    type="warning"
                    showIcon
                    message="Nu există un șablon activ pentru necesarul de materiale."
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
                <Card title="Necesar de materiale nou">
                  <Alert
                    type="info"
                    showIcon
                    message="Numărul și data se generează automat la emitere."
                    description="Lucrarea este preluată din proiect, iar compartimentul / șantierul se completează manual. Necesarul nu are furnizor; după aprobare, aprovizionarea emite notele de comandă către furnizori."
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
                    <LiniiNecesar />
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
            key: 'note-comanda',
            label: 'Note de comandă',
            children: <ListaNoteComanda poateCrea={poateCrea} onNotaNoua={() => setTab('nota-noua')} />,
          },
          {
            key: 'nota-noua',
            label: 'Notă de comandă nouă',
            children: <NotaComandaNoua poateCrea={poateCrea} />,
          },
          {
            key: 'import',
            label: 'Import document extern',
            children: (
              <Space direction="vertical" style={{ width: '100%' }} size="large">
                <Card
                  title="Import document emis în afara sistemului"
                  extra={
                    <Select
                      value={tipImport}
                      onChange={setTipImport}
                      style={{ width: 220 }}
                      options={[
                        { value: 'necesar-materiale', label: 'Necesar de materiale' },
                        { value: 'nota-comanda', label: 'Notă de comandă' },
                        { value: 'factura-primita', label: 'Factură primită' },
                      ]}
                    />
                  }
                >
                  {tipImport === 'necesar-materiale' ? (
                    <ImportNecesarExtern poateCrea={poateCrea} />
                  ) : tipImport === 'nota-comanda' ? (
                    <ImportNotaComanda poateCrea={poateCrea} />
                  ) : (
                    <Alert
                      type="info"
                      showIcon
                      title="Facturile primite se importă în modulul Facturi"
                      description="Acolo se citesc automat din XML-ul e-Factura (sau arhiva ZIP din SPV) ori din PDF / scanare, se înregistrează pe proiect, iar furnizorul se adaugă în nomenclator."
                      action={<Button type="primary" onClick={() => navigheaza('/facturi?tab=import')}>Deschide importul de facturi</Button>}
                    />
                  )}
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
                      { title: 'Tip', dataIndex: 'codTip', render: (cod: string) => cod === 'factura-primita' ? 'Factură primită' : 'Necesar de materiale' },
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
        title={`Respinge necesarul ${notaSelectata?.numar ?? ''}`}
        open={Boolean(notaSelectata)}
        onCancel={() => {
          setNotaSelectata(undefined)
          formRespingere.resetFields()
        }}
        onOk={() => void formRespingere.submit()}
        confirmLoading={respingeNota.isPending}
        okText="Respinge necesarul"
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
