import { CheckOutlined, CloseCircleOutlined, DeleteOutlined, EditOutlined, FileAddOutlined, FileExcelOutlined, FlagOutlined, HistoryOutlined, PaperClipOutlined, UploadOutlined } from '@ant-design/icons'
import {
  Alert, App, Button, Card, Checkbox, Col, DatePicker, Descriptions, Drawer, Empty, Flex, Form, Input, Modal, Popconfirm, Progress, Row, Select, Space, Spin,
  Statistic, Table, Tabs, Tag, Timeline, Typography, Upload,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  stariContract, tipuriContract, useActiveazaContract, useAdaugaActAditional, useContract, useIncarcaDocumentContract, useInchideContract, useSemneazaAct,
  useStergeContract,
} from '../../api/contracte'
import type { Contract } from '../../api/contracte'
import { api } from '../../api/client'
import { descarcaFisier } from '../../util/pdf'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatData, formatLei } from '../../util/format'
import { EditorContract } from './EditorContract'
import { FacturiPlatiContract } from './FacturiPlatiContract'
import { RegistruFisa } from './RegistreFisa'

const pct = (f: number) => `${(f * 100).toLocaleString('ro-RO', { maximumFractionDigits: 4 })}%`

/** Bara „facturat din contract”: verde, portocaliu peste 90%, roșu peste valoare. */
export function ProgresContract({ c, mic }: { c: Contract; mic?: boolean }) {
  const s = c.situatie
  return (
    <Progress
      size={mic ? 'small' : 'default'}
      percent={Math.min(100, s.procentFacturat)}
      status={s.depasit ? 'exception' : 'normal'}
      strokeColor={s.depasit ? undefined : s.procentFacturat >= 90 ? '#fa8c16' : '#52c41a'}
      format={() => `${s.procentFacturat.toLocaleString('ro-RO')}%`}
    />
  )
}

/**
 * Fișa contractului: datele, situația (valoare actualizată, facturat, rest, garanția reținută, termen), pozițiile, actele
 * adiționale, facturile legate și documentele; acțiunile după stare (ciornă: modifică / șterge / activează; activ: act
 * adițional, finalizare, reziliere).
 */
export function DetaliiContract({ proiectId, id, onInchide, permisiuni }: {
  proiectId?: string; id?: string; onInchide: () => void; permisiuni: { editare: boolean; aprobare: boolean }
}) {
  const { message } = App.useApp()
  const date = useContract(proiectId, id)
  const c = date.data
  const activeaza = useActiveazaContract()
  const sterge = useStergeContract()
  const incarca = useIncarcaDocumentContract()
  const semneaza = useSemneazaAct()
  const [editare, setEditare] = useState(false)
  const [act, setAct] = useState(false)
  const [inchidere, setInchidere] = useState<'Finalizat' | 'Reziliat'>()

  const executa = async (f: () => Promise<unknown>, mesaj: string) => {
    try {
      await f()
      message.success(mesaj)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const [seExporta, setSeExporta] = useState(false)
  /** Fișa de urmărire în formatul Excel al fișelor (D-56, etapa 4). */
  const exportaFisa = async () => {
    if (!c) return
    setSeExporta(true)
    try {
      const nume = `Fisa_Contract_${(c.numarExtern ?? c.numar).replace(/[^\p{L}\p{N}]+/gu, '_')}_${c.partenerDenumire.replace(/[^\p{L}\p{N}]+/gu, '_')}.xlsx`
      descarcaFisier(await api.getBinar(`/api/contracte/proiecte/${c.proiectId}/${c.id}/fisa.xlsx`), nume)
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeExporta(false)
    }
  }

  const descarca = async (documentId: string) => {
    try {
      const blob = await api.getBinar(`/api/contracte/proiecte/${proiectId}/${id}/documente/${documentId}`)
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const deschis = c?.stare === 'Activ'
  return (
    <Drawer
      open={!!id}
      onClose={onInchide}
      size={1150}
      destroyOnHidden
      title={c && (
        <Space wrap>
          <span>{c.numar}{c.numarExtern ? ` (nr. ${c.numarExtern})` : ''} — {c.partenerDenumire}</span>
          <Tag color={stariContract[c.stare].culoare}>{stariContract[c.stare].eticheta}</Tag>
        </Space>
      )}
      extra={c && (
        <Space wrap>
          <Button icon={<FileExcelOutlined />} loading={seExporta} onClick={() => void exportaFisa()}>Fișa Excel</Button>
          {c.stare === 'Ciorna' && (
            <>
              <Button icon={<EditOutlined />} disabled={!permisiuni.editare} onClick={() => setEditare(true)}>Modifică</Button>
              <Popconfirm title={`Ștergi ciorna ${c.numar}?`} okText="Șterge" cancelText="Nu"
                onConfirm={() => executa(async () => { await sterge.mutateAsync({ proiectId: c.proiectId, id: c.id }); onInchide() }, `Ciorna ${c.numar} a fost ștearsă.`)}>
                <Button danger icon={<DeleteOutlined />} disabled={!permisiuni.editare}>Șterge</Button>
              </Popconfirm>
              <Popconfirm title="Contractul a fost semnat?" description="După activare nu se mai modifică: schimbările se fac prin acte adiționale, iar facturile partenerului se leagă de el."
                okText="Activează" cancelText="Nu" onConfirm={() => executa(() => activeaza.mutateAsync({ proiectId: c.proiectId, id: c.id }), `Contractul ${c.numar} e activ.`)}>
                <Button type="primary" icon={<CheckOutlined />} disabled={!permisiuni.aprobare} loading={activeaza.isPending}>Activează (semnat)</Button>
              </Popconfirm>
            </>
          )}
          {deschis && (
            <>
              <Button icon={<EditOutlined />} disabled={!permisiuni.editare} onClick={() => setEditare(true)}>Corectează</Button>
              <Button icon={<FileAddOutlined />} disabled={!permisiuni.editare} onClick={() => setAct(true)}>Act adițional</Button>
              <Button icon={<FlagOutlined />} disabled={!permisiuni.aprobare} onClick={() => setInchidere('Finalizat')}>Finalizează</Button>
              <Button danger icon={<CloseCircleOutlined />} disabled={!permisiuni.aprobare} onClick={() => setInchidere('Reziliat')}>Reziliază</Button>
            </>
          )}
        </Space>
      )}
    >
      {!c ? (
        <Flex justify="center" style={{ padding: 48 }}><Spin /></Flex>
      ) : (
        <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
          {c.stare === 'Ciorna' && <Alert type="info" showIcon title="Ciornă (inclusiv contractul importat): se modifică liber. După semnare apasă „Activează”; apoi se corectează doar cu motiv sau prin acte adiționale." />}
          {c.inchidere && (
            <Alert type={c.inchidere.tip === 'Reziliat' ? 'error' : 'success'} showIcon
              title={`${c.inchidere.tip === 'Reziliat' ? 'Reziliat' : 'Finalizat'} la ${formatData(c.inchidere.data)} de ${c.inchidere.inchisDe}`}
              description={c.inchidere.motiv ?? 'Nu se mai leagă facturi noi de el.'} />
          )}
          {c.situatie.depasit && <Alert type="error" showIcon title={`Facturat peste valoarea contractului cu ${formatLei(-c.situatie.rest)} ${c.moneda}: e nevoie de un act adițional.`} />}
          {(c.situatieFisa?.alerte.length ?? 0) > 0 ? (
            <Alert type="warning" showIcon title={`${c.situatieFisa!.alerte.length} de urmărit`}
              description={<ul style={{ margin: 0, paddingInlineStart: 18 }}>{c.situatieFisa!.alerte.map((a) => <li key={a.mesaj}>{a.mesaj}</li>)}</ul>} />
          ) : c.situatie.termenDepasit && <Alert type="warning" showIcon title={`Termenul de finalizare (${formatData(c.situatie.termen)}) a trecut.`} />}

          <Card size="small">
            <Row gutter={16}>
              <Col xs={12} md={5}><Statistic title="Valoare actualizată" value={c.situatie.valoareActualizata} formatter={(v) => formatLei(Number(v))} suffix={c.moneda} /></Col>
              <Col xs={12} md={5}><Statistic title="Facturat" value={c.situatie.facturat} formatter={(v) => formatLei(Number(v))} /></Col>
              <Col xs={12} md={5}><Statistic title="Rest de facturat" value={c.situatie.rest} formatter={(v) => formatLei(Number(v))} valueStyle={c.situatie.rest < 0 ? { color: '#cf1322' } : undefined} /></Col>
              <Col xs={12} md={5}><Statistic title={`Avans contractual${(c.conditii?.transeAvans.length ?? 0) > 0 ? ` (${c.conditii!.transeAvans.map((t) => pct(t.procent)).join(' + ')})` : ''}`} value={c.avans} formatter={(v) => formatLei(Number(v))} /></Col>
              <Col xs={24} md={4}><Typography.Text type="secondary">Facturat din contract</Typography.Text><ProgresContract c={c} /></Col>
            </Row>
          </Card>

          <Tabs size="small" items={[
            { key: 'generale', label: '1. Date generale', children: <DateGenerale c={c} /> },
            { key: 'valoare', label: '2. Valoare contract', children: (
              <ValoareContract c={c} poateSemna={permisiuni.aprobare}
                onSemneaza={(actId, data) => executa(() => semneaza.mutateAsync({ proiectId: c.proiectId, id: c.id, actId, data }), 'Actul adițional a fost semnat: intră în valoarea contractului.')} />
            ) },
            { key: 'obligatii', label: `3. Obligații și termene (${c.fisa?.obligatii.length ?? 0})`, children: <RegistruFisa contract={c} registru="obligatii" poateEdita={permisiuni.editare} randuri={(c.fisa?.obligatii ?? []) as unknown as Record<string, unknown>[]} /> },
            { key: 'facturi', label: `4. Facturi și plăți (${c.facturiFisa?.length ?? 0})`, children: <FacturiPlatiContract c={c} poateEdita={permisiuni.editare} /> },
            { key: 'garantii', label: `5. Garanții și riscuri (${(c.fisa?.instrumente.length ?? 0) + (c.fisa?.riscuri.length ?? 0)})`, children: (
              <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
                <Garantii c={c} />
                <RegistruFisa contract={c} registru="instrumente" poateEdita={permisiuni.editare} randuri={(c.fisa?.instrumente ?? []) as unknown as Record<string, unknown>[]} />
                <RegistruFisa contract={c} registru="riscuri" poateEdita={permisiuni.editare} randuri={(c.fisa?.riscuri ?? []) as unknown as Record<string, unknown>[]} />
              </Space>
            ) },
            { key: 'actiuni', label: `6. Acțiuni (${c.fisa?.actiuni.filter((a) => a.stare !== 'Inchisa').length ?? 0} deschise)`, children: <RegistruFisa contract={c} registru="actiuni" poateEdita={permisiuni.editare} randuri={(c.fisa?.actiuni ?? []) as unknown as Record<string, unknown>[]} /> },
            { key: 'surse', label: `7. Surse (${(c.fisa?.surse?.length ?? 0) + (c.documente?.length ?? 0)})`, children: (
              <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
              <RegistruFisa contract={c} registru="surse" poateEdita={permisiuni.editare} randuri={(c.fisa?.surse ?? []) as unknown as Record<string, unknown>[]} />
              <Card size="small" title="Documente (contractul semnat, acte adiționale, facturi, garanții, anexe)"
                extra={(
                  <Upload showUploadList={false} disabled={!permisiuni.editare} beforeUpload={(f) => {
                    void executa(() => incarca.mutateAsync({ proiectId: c.proiectId, id: c.id, fisier: f }), `${f.name} a fost atașat.`)
                    return false
                  }}>
                    <Button size="small" icon={<UploadOutlined />} disabled={!permisiuni.editare} loading={incarca.isPending}>Atașează</Button>
                  </Upload>
                )}>
                {(c.documente ?? []).length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Niciun document atașat." /> : (
                  <Space orientation="vertical">
                    {(c.documente ?? []).map((d) => (
                      <Button key={d.id} type="link" icon={<PaperClipOutlined />} onClick={() => void descarca(d.id)} style={{ padding: 0 }}>
                        {d.numeFisier} <Typography.Text type="secondary"> · {formatData(d.incarcatLa)}, {d.incarcatDe}</Typography.Text>
                      </Button>
                    ))}
                  </Space>
                )}
              </Card>
              </Space>
            ) },
            { key: 'faze', label: `8. Termene de execuție (${c.fisa?.faze.length ?? 0})`, children: <RegistruFisa contract={c} registru="faze" poateEdita={permisiuni.editare} randuri={(c.fisa?.faze ?? []) as unknown as Record<string, unknown>[]} /> },
            ...((c.fisa?.corecturi.length ?? 0) > 0 ? [{
              key: 'istoric', label: <span><HistoryOutlined /> Corecturi ({c.fisa!.corecturi.length})</span>, children: (
                <Timeline items={c.fisa!.corecturi.map((k) => ({
                  children: (
                    <>
                      <Typography.Text strong>{formatData(k.la)} · {k.corectatDe}</Typography.Text> — {k.motiv}
                      <ul style={{ margin: '4px 0 0', paddingInlineStart: 18 }}>{k.modificari.map((m) => <li key={m}><Typography.Text type="secondary">{m}</Typography.Text></li>)}</ul>
                    </>
                  ),
                }))} />
              ),
            }] : []),
          ]} />
        </Space>
      )}

      {c && <EditorContract deschis={editare} contract={c} onInchide={() => setEditare(false)} />}
      {c && <ActAditionalNou contract={c} deschis={act} onInchide={() => setAct(false)} />}
      {c && <InchidereContract contract={c} tip={inchidere} onInchide={() => setInchidere(undefined)} />}
    </Drawer>
  )
}

function ActAditionalNou({ contract, deschis, onInchide }: { contract: Contract; deschis: boolean; onInchide: () => void }) {
  const { message } = App.useApp()
  const adauga = useAdaugaActAditional()
  const [form] = Form.useForm<{ numarExtern?: string; data: Dayjs; obiect: string; diferentaValoare?: number | null; dataSfarsitNoua?: Dayjs | null; semnat: boolean }>()
  return (
    <Modal open={deschis} onCancel={onInchide} title={`Act adițional la ${contract.numar}`} okText="Adaugă" cancelText="Renunță" onOk={() => form.submit()}
      confirmLoading={adauga.isPending} destroyOnHidden>
      <Form form={form} layout="vertical" preserve={false} initialValues={{ data: dayjs(), semnat: true }} onFinish={async (v) => {
        try {
          await adauga.mutateAsync({ proiectId: contract.proiectId, id: contract.id, date: {
            numarExtern: v.numarExtern?.trim() || null, data: v.data.format('YYYY-MM-DD'), obiect: v.obiect.trim(),
            diferentaValoare: Number(v.diferentaValoare ?? 0), dataSfarsitNoua: v.dataSfarsitNoua?.format('YYYY-MM-DD') ?? null, semnat: v.semnat,
          } })
          message.success(v.semnat ? 'Actul adițional a fost adăugat.' : 'Actul adițional propus a fost adăugat: intră în valoare după semnare.')
          onInchide()
        } catch (e) {
          message.error((e as Error).message)
        }
      }}>
        <Row gutter={12}>
          <Col span={12}><Form.Item name="numarExtern" label="Nr. pe act"><Input maxLength={50} /></Form.Item></Col>
          <Col span={12}><Form.Item name="data" label="Data" rules={[{ required: true }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
        </Row>
        <Form.Item name="obiect" label="Obiectul actului adițional" rules={[{ required: true, whitespace: true }]}>
          <Input.TextArea rows={2} maxLength={1000} placeholder="ex. Lucrări suplimentare corp B" />
        </Form.Item>
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item name="diferentaValoare" label={`Diferența de valoare (${contract.moneda}, fără TVA)`} extra="Negativ pentru renunțări.">
              <NumarRo style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="dataSfarsitNoua" label="Termen nou de finalizare">
              <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="semnat" valuePropName="checked" extra="Debifat = act propus (nesemnat): apare ca valoare propusă și nu schimbă valoarea / termenul până la semnare.">
          <Checkbox>Actul e semnat</Checkbox>
        </Form.Item>
      </Form>
    </Modal>
  )
}

function InchidereContract({ contract, tip, onInchide }: { contract: Contract; tip?: 'Finalizat' | 'Reziliat'; onInchide: () => void }) {
  const { message } = App.useApp()
  const inchide = useInchideContract()
  const [form] = Form.useForm<{ tip: 'Finalizat' | 'Reziliat'; data: Dayjs; motiv?: string }>()
  const tipAles = Form.useWatch('tip', form) ?? tip
  return (
    <Modal open={!!tip} onCancel={onInchide} title={`${tip === 'Reziliat' ? 'Reziliere' : 'Finalizare'}: ${contract.numar}`}
      okText={tip === 'Reziliat' ? 'Reziliază' : 'Finalizează'} okButtonProps={{ danger: tip === 'Reziliat' }} cancelText="Renunță" onOk={() => form.submit()}
      confirmLoading={inchide.isPending} destroyOnHidden>
      <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="După închidere nu se mai leagă facturi noi și nu se mai încheie acte adiționale." />
      <Form form={form} layout="vertical" preserve={false} initialValues={{ tip, data: dayjs() }} onFinish={async (v) => {
        try {
          await inchide.mutateAsync({ proiectId: contract.proiectId, id: contract.id, date: { tip: v.tip, data: v.data.format('YYYY-MM-DD'), motiv: v.motiv?.trim() || null } })
          message.success(`Contractul ${contract.numar} a fost ${v.tip === 'Reziliat' ? 'reziliat' : 'finalizat'}.`)
          onInchide()
        } catch (e) {
          message.error((e as Error).message)
        }
      }}>
        <Row gutter={12}>
          <Col span={12}><Form.Item name="tip" label="Închidere"><Select options={[{ value: 'Finalizat', label: 'Finalizat (recepționat)' }, { value: 'Reziliat', label: 'Reziliat' }]} /></Form.Item></Col>
          <Col span={12}><Form.Item name="data" label="Data" rules={[{ required: true }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} /></Form.Item></Col>
        </Row>
        <Form.Item name="motiv" label={tipAles === 'Reziliat' ? 'Motivul rezilierii' : 'Observații (ex. procesul-verbal de recepție)'}
          rules={[{ required: tipAles === 'Reziliat', whitespace: true, message: 'Motivul rezilierii este obligatoriu.' }]}>
          <Input.TextArea rows={2} maxLength={1000} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** Foaia „1. Date generale”. */
function DateGenerale({ c }: { c: Contract }) {
  const k = c.conditii
  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Descriptions size="small" bordered column={{ xs: 1, md: 2 }} items={[
        { label: 'Contract', children: `${c.numarExtern ?? c.numar} / ${formatData(c.dataSemnarii)}` },
        { label: 'Subantreprenor', children: `${c.partenerDenumire}${c.partenerCui ? ` (CUI ${c.partenerCui})` : ''}` },
        { label: 'Antreprenori', children: k && k.membri.length > 0 ? k.membri.map((m) => `${m.denumire} (${pct(m.cota)})`).join(' și ') : 'firma (singură)' },
        { label: 'Proiect', children: c.proiectCod },
        { label: 'Obiect', children: c.obiect, span: 2 },
        ...(k?.amplasament ? [{ label: 'Clădiri / amplasament', children: k.amplasament, span: 2 }] : []),
        { label: 'Tip contract', children: tipuriContract[c.tip] },
        { label: 'Tip preț', children: k?.tipPret ?? (c.linii.length > 0 ? 'Pe poziții (prețuri unitare)' : 'Valoare globală') },
        { label: 'Monedă', children: `${c.moneda} fără TVA` },
        { label: 'Valoare inițială', children: `${formatLei(c.valoare)} ${c.moneda}` },
        { label: 'Perioada', children: `${formatData(c.dataInceput)} – ${formatData(c.situatie.termen)}${c.situatie.termen !== c.dataSfarsit ? ' (prelungit)' : ''}` },
        { label: 'Durata', children: k?.durata ?? '—' },
        { label: 'Predare amplasament', children: formatData(k?.dataPredareAmplasament ?? null) },
        { label: 'PVRTL', children: formatData(k?.dataPvrtl ?? null) },
        { label: 'Stadiu', children: k?.stareText ?? '—', span: 2 },
        { label: 'Plata', children: c.termenPlataZile !== null ? `la ${c.termenPlataZile} zile de la comunicarea facturii` : '—' },
        { label: 'TVA', children: c.taxareInversa ? 'taxare inversă' : 'normal' },
        { label: 'Semnat / activat', children: c.activatLa ? `${formatData(c.activatLa)} (${c.activatDe})` : '—' },
        ...(c.observatii ? [{ label: 'Observații', children: c.observatii, span: 2 }] : []),
      ]} />
      {c.linii.length > 0 && (
        <Table size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={c.linii} title={() => <Typography.Text strong>Poziții</Typography.Text>}
          columns={[
            { title: 'Poziție', dataIndex: 'denumire' },
            { title: 'Cantitate', width: 140, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
            { title: 'Preț unitar', dataIndex: 'pretUnitar', width: 130, align: 'right', render: (v: number) => formatLei(v) },
            { title: 'Valoare', dataIndex: 'valoare', width: 140, align: 'right', render: (v: number) => formatLei(v) },
          ]} />
      )}
    </Space>
  )
}

/** Membrii asocierii (sau firma singură, 100%) pentru coloanele pe antreprenor. */
const membri = (c: Contract) => (c.conditii && c.conditii.membri.length > 0 ? c.conditii.membri : [{ denumire: 'Firma', cui: null, cota: 1, esteFirmaActiva: true }])

/** Foaia „2. Valoare contract”: contractul și actele adiționale (semnate / propuse), pe fiecare membru al asocierii și total. */
function ValoareContract({ c, poateSemna, onSemneaza }: { c: Contract; poateSemna: boolean; onSemneaza: (actId: string, data: string) => void }) {
  const m = membri(c)
  const randuri = [
    { cheie: 'c', document: `Contract ${c.numarExtern ?? c.numar} / ${formatData(c.dataSemnarii)}`, modificare: c.valoare, cumulat: c.valoare, stare: 'Semnat', semnat: true, id: '' },
    ...(c.acte ?? []).reduce<{ lista: { cheie: string; document: string; modificare: number; cumulat: number; stare: string; semnat: boolean; id: string }[]; total: number }>((acc, a) => {
      const total = acc.total + a.diferentaValoare
      acc.lista.push({ cheie: a.id, document: `AA${a.numar}${a.numarExtern ? ` (nr. ${a.numarExtern})` : ''} / ${formatData(a.data)} — ${a.obiect}`, modificare: a.diferentaValoare,
        cumulat: total, stare: a.semnat ? 'Semnat' : 'Nesemnat — propunere', semnat: a.semnat, id: a.id })
      return { lista: acc.lista, total }
    }, { lista: [], total: c.valoare }).lista,
  ]
  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Table size="small" pagination={false} rowKey="cheie" dataSource={randuri}
        onRow={(r) => ({ style: r.semnat ? undefined : { background: '#FFF1D6' } })}
        summary={() => (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0}><b>Valoare contractuală (semnată)</b>{c.situatie.valoarePropusa !== c.situatie.valoareActualizata ? <Typography.Text type="secondary"> · propusă {formatLei(c.situatie.valoarePropusa)}</Typography.Text> : null}</Table.Summary.Cell>
            <Table.Summary.Cell index={1} />
            <Table.Summary.Cell index={2} align="right"><b>{formatLei(c.situatie.valoareActualizata)}</b></Table.Summary.Cell>
            <Table.Summary.Cell index={3} colSpan={2} />
          </Table.Summary.Row>
        )}
        columns={[
          { title: 'Document', dataIndex: 'document' },
          { title: `Modificare (${c.moneda})`, dataIndex: 'modificare', width: 150, align: 'right', render: (v: number) => formatLei(v) },
          { title: `Valoare cumulată (${c.moneda})`, dataIndex: 'cumulat', width: 170, align: 'right', render: (v: number) => formatLei(v) },
          { title: 'Statut', dataIndex: 'stare', width: 170, render: (v: string, r) => (r.semnat ? v : <Tag color="gold">{v}</Tag>) },
          { title: '', width: 110, render: (_, r) => !r.semnat && poateSemna && (
            <Popconfirm title="Actul adițional a fost semnat azi?" okText="Semnat" cancelText="Nu" onConfirm={() => onSemneaza(r.id, dayjs().format('YYYY-MM-DD'))}>
              <Button size="small" icon={<CheckOutlined />}>Semnează</Button>
            </Popconfirm>
          ) },
        ]} />
      <Table size="small" pagination={false} rowKey="denumire" dataSource={m} title={() => <Typography.Text strong>Pe antreprenor</Typography.Text>}
        columns={[
          { title: 'Antreprenor', render: (_, x) => <>{x.denumire}{x.esteFirmaActiva && m.length > 1 ? <Tag style={{ marginInlineStart: 6 }}>firma</Tag> : null}</> },
          { title: 'Cotă', width: 80, align: 'right', render: (_, x) => pct(x.cota) },
          { title: `Valoare actuală (${c.moneda})`, width: 170, align: 'right', render: (_, x) => formatLei(Math.round(c.situatie.valoareActualizata * x.cota * 100) / 100) },
          ...((c.conditii?.transeAvans ?? []).map((t) => ({
            title: `Avans tranșa ${t.numar} (${pct(t.procent)})`, width: 160, align: 'right' as const,
            render: (_: unknown, x: { cota: number }) => formatLei(Math.round(c.valoare * t.procent * x.cota * 100) / 100),
          }))),
        ]} />
    </Space>
  )
}

/** Garanțiile contractuale (din procente) pe antreprenor, ca în foaia „5. Garanții și riscuri”. */
function Garantii({ c }: { c: Contract }) {
  const k = c.conditii
  const v = c.situatie.valoareActualizata
  const randuri = [
    { cheie: 'av', denumire: 'Garanție restituire avans', procent: k?.procentGarantieAvans ?? 0, conditie: 'pe durata execuției / până la recuperarea avansului' },
    { cheie: 'be', denumire: 'Garanție de bună execuție', procent: c.procentGarantie, conditie: '' },
    { cheie: 'gt', denumire: 'Garanție tehnică', procent: k?.procentGarantieTehnica ?? 0, conditie: k?.luniGarantieTehnica ? `${k.luniGarantieTehnica} luni de la PVRTL${k.dataPvrtl ? ` (până la ${formatData(dayjs(k.dataPvrtl).add(k.luniGarantieTehnica, 'month').format('YYYY-MM-DD'))})` : ''}` : '' },
  ].filter((r) => r.procent > 0)
  const m = membri(c)
  return (
    <Space orientation="vertical" size="small" style={{ width: '100%' }}>
      <Table size="small" pagination={false} rowKey="cheie" dataSource={randuri} title={() => <Typography.Text strong>Garanții contractuale</Typography.Text>}
        locale={{ emptyText: 'Fără procente de garanție în contract.' }}
        columns={[
          { title: 'Garanție', dataIndex: 'denumire', width: 220 },
          { title: '%', width: 70, align: 'right', render: (_, r) => pct(r.procent) },
          { title: `Total (${c.moneda})`, width: 150, align: 'right', render: (_, r) => formatLei(Math.round(v * r.procent * 100) / 100) },
          ...(m.length > 1 ? m.map((x) => ({ title: x.denumire, width: 160, align: 'right' as const, render: (_: unknown, r: { procent: number }) => formatLei(Math.round(v * r.procent * x.cota * 100) / 100) })) : []),
          { title: 'Valabilitate', dataIndex: 'conditie' },
        ]} />
      {k && (k.penalitateExecutieZi > 0 || k.penalitatePlataZi > 0) && (
        <Typography.Text type="secondary">
          Penalități: {k.penalitateExecutieZi > 0 ? `întârziere execuție ${pct(k.penalitateExecutieZi)}/zi${k.plafonPenalitateExecutie > 0 ? `, plafon ${pct(k.plafonPenalitateExecutie)}` : ''}` : ''}
          {k.penalitateExecutieZi > 0 && k.penalitatePlataZi > 0 ? '; ' : ''}
          {k.penalitatePlataZi > 0 ? `întârziere plată ${pct(k.penalitatePlataZi)}/zi${k.plafonPenalitatePlata > 0 ? `, plafon ${pct(k.plafonPenalitatePlata)}` : ''}` : ''} (din valoarea contractului).
        </Typography.Text>
      )}
    </Space>
  )
}

