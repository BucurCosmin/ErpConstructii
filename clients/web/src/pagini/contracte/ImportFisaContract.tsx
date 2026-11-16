import { DeleteOutlined, InboxOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Col, DatePicker, Form, Input, Modal, Radio, Row, Select, Space, Spin, Table, Tabs, Tag, Typography, Upload } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { analizeazaFisaContract, instrumentePlata, prioritati, stariActiune, tipuriContract, tipuriFacturaContract, tipuriInstrument, useImportFisaContract, useParteneriContract } from '../../api/contracte'
import type { CampCitit, FisaCitita, TipContract } from '../../api/contracte'
import { useProiecte } from '../../api/interogari'
import { NumarRo } from '../../componente/NumarRo'
import { etichetaProiect, formatData, formatLei } from '../../util/format'

const PARTENER_NOU = '__nou__'
const pct = (f: number) => `${(f * 100).toLocaleString('ro-RO', { maximumFractionDigits: 4 })}%`
const lei = (v: number | null | undefined) => (v === null || v === undefined ? '—' : formatLei(v))

/** Eticheta câmpului citit: nesigur / negăsit în fișă. */
function Certitudine({ c }: { c: CampCitit<unknown> }) {
  if (c.certitudine === 'Sigur') return null
  return <Tag color={c.certitudine === 'Lipsa' ? 'red' : 'orange'} style={{ marginInlineStart: 6 }}>{c.certitudine === 'Lipsa' ? 'negăsit' : 'de verificat'}</Tag>
}

interface Valori {
  proiectId?: string
  partenerId?: string
  partenerDenumire?: string
  partenerCui?: string
  numarExtern?: string
  dataSemnarii?: Dayjs
  tip: TipContract
  obiect?: string
  moneda: string
  valoare?: number | null
  membri: { denumire?: string; cui?: string; cota?: number | null }[]
  firmaActiva?: number
}

/**
 * Importul fișei de urmărire a contractului din Excel (D-56, etapa 3): fișierul se citește, datele de bază se verifică
 * (proiectul, subantreprenorul, numărul, valoarea, asocierea), restul fișei se vede pe tab-uri; confirmarea creează
 * contractul ca ciornă (editabil), cu tot conținutul fișei și fișierul original atașat.
 */
export function ImportFisaContract({ deschis, proiectId, onInchide, onImportat }: {
  deschis: boolean; proiectId?: string; onInchide: () => void; onImportat: (id: string, proiectId: string) => void
}) {
  const { message, modal } = App.useApp()
  const [form] = Form.useForm<Valori>()
  const proiecte = useProiecte()
  const parteneri = useParteneriContract()
  const importa = useImportFisaContract()
  const [fisier, setFisier] = useState<File>()
  const [fisa, setFisa] = useState<FisaCitita>()
  const [seCiteste, setSeCiteste] = useState(false)
  const partenerId = Form.useWatch('partenerId', form)

  const inchide = () => {
    setFisier(undefined)
    setFisa(undefined)
    form.resetFields()
    onInchide()
  }

  const citeste = async (f: File) => {
    setSeCiteste(true)
    try {
      const r = await analizeazaFisaContract(f)
      setFisier(f)
      setFisa(r)
      // Subantreprenorul: cel din nomenclator cu același CUI / aceeași denumire, altfel unul nou.
      const cui = r.partenerCui.valoare?.replace(/^RO/i, '')
      const gasit = (parteneri.data ?? []).find((p) => (cui && p.cui?.replace(/^RO/i, '') === cui)
        || (r.partenerDenumire.valoare && p.denumire.toUpperCase() === r.partenerDenumire.valoare.toUpperCase()))
      const membri = r.conditii.membri
      form.setFieldsValue({
        proiectId,
        partenerId: gasit?.id ?? PARTENER_NOU,
        partenerDenumire: r.partenerDenumire.valoare ?? undefined,
        partenerCui: r.partenerCui.valoare ?? undefined,
        numarExtern: r.numarExtern.valoare ?? undefined,
        dataSemnarii: r.dataSemnarii.valoare ? dayjs(r.dataSemnarii.valoare) : undefined,
        tip: 'Subantreprenoriat',
        obiect: r.obiect.valoare ?? undefined,
        moneda: r.moneda.valoare ?? 'RON',
        valoare: r.valoare.valoare,
        membri: membri.map((m) => ({ denumire: m.denumire, cui: m.cui ?? undefined, cota: Math.round(m.cota * 10000) / 100 })),
        firmaActiva: membri.findIndex((m) => m.esteFirmaActiva) >= 0 ? membri.findIndex((m) => m.esteFirmaActiva) : undefined,
      })
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeCiteste(false)
    }
  }

  const confirma = async () => {
    if (!fisier || !fisa) return
    const v = await form.validateFields()
    const pid = proiectId ?? v.proiectId
    if (!pid) return
    try {
      const r = await importa.mutateAsync({
        proiectId: pid, fisier,
        date: {
          numarExtern: v.numarExtern?.trim() || null,
          dataSemnarii: v.dataSemnarii!.format('YYYY-MM-DD'),
          partenerId: v.partenerId && v.partenerId !== PARTENER_NOU ? v.partenerId : null,
          partenerDenumire: v.partenerId === PARTENER_NOU ? v.partenerDenumire?.trim() || null : null,
          partenerCui: v.partenerId === PARTENER_NOU ? v.partenerCui?.trim() || null : null,
          tip: v.tip,
          obiect: (v.obiect ?? '').trim(),
          moneda: v.moneda,
          valoare: Number(v.valoare ?? 0),
          membri: (v.membri ?? []).filter((m) => m?.denumire?.trim()).map((m, i) => ({
            denumire: m.denumire!.trim(), cui: m.cui?.trim() || null, cota: Number(m.cota ?? 0) / 100, esteFirmaActiva: v.firmaActiva === i,
          })),
        },
      })
      if (r.avertismente.length > 0)
        modal.info({ title: `Contractul ${r.numar} a fost creat (ciornă)`, width: 640,
          content: <><Typography.Paragraph>De verificat în fișă:</Typography.Paragraph><ul>{r.avertismente.map((a) => <li key={a}>{a}</li>)}</ul></> })
      else
        message.success(`Contractul ${r.numar} a fost creat (ciornă) din fișă. Verifică-l, apoi „Activează”.`)
      inchide()
      onImportat(r.id, pid)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal open={deschis} onCancel={inchide} width="min(1200px, 96vw)" title="Importă fișa de urmărire a contractului (Excel)"
      okText="Creează contractul (ciornă)" okButtonProps={{ disabled: !fisa }} cancelText="Renunță" onOk={() => void confirma()} confirmLoading={importa.isPending} destroyOnHidden>
      {!fisa ? (
        <Spin spinning={seCiteste}>
          <Upload.Dragger accept=".xlsx" showUploadList={false} beforeUpload={(f) => { void citeste(f); return false }}>
            <p className="ant-upload-drag-icon"><InboxOutlined /></p>
            <p className="ant-upload-text">Trage aici fișa contractului (.xlsx) sau apasă pentru a o alege</p>
            <p className="ant-upload-hint">Formatul fișelor de urmărire: Date generale, Valoare contract, Obligații, Facturi / Situații, Garanții, Acțiuni, Surse, Termene.</p>
          </Upload.Dragger>
        </Spin>
      ) : (
        <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
          {fisa.note.length > 0 && (
            <Alert type="warning" showIcon title="De verificat" description={<ul style={{ margin: 0, paddingInlineStart: 18 }}>{fisa.note.map((n) => <li key={n}>{n}</li>)}</ul>} />
          )}
          <Form<Valori> form={form} layout="vertical" requiredMark="optional">
            <Row gutter={12}>
              {!proiectId && (
                <Col xs={24} md={12}>
                  <Form.Item name="proiectId" label={<>Proiect{fisa.proiect.valoare ? <Typography.Text type="secondary"> (în fișă: {fisa.proiect.valoare})</Typography.Text> : null}</>} rules={[{ required: true, message: 'Alege proiectul.' }]}>
                    <Select showSearch={{ optionFilterProp: 'label' }} options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
                  </Form.Item>
                </Col>
              )}
              <Col xs={24} md={12}>
                <Form.Item name="partenerId" label={<>Subantreprenor<Certitudine c={fisa.partenerDenumire} /></>} rules={[{ required: true }]}>
                  <Select showSearch={{ optionFilterProp: 'label' }} options={[
                    { value: PARTENER_NOU, label: `➕ Nou: ${fisa.partenerDenumire.valoare ?? ''}${fisa.partenerCui.valoare ? ` (${fisa.partenerCui.valoare})` : ''}` },
                    ...(parteneri.data ?? []).map((p) => ({ value: p.id, label: `${p.denumire}${p.cui ? ` (${p.cui})` : ''}` })),
                  ]} />
                </Form.Item>
              </Col>
              {partenerId === PARTENER_NOU && (
                <>
                  <Col xs={24} md={16}><Form.Item name="partenerDenumire" label="Denumirea subantreprenorului" rules={[{ required: true, whitespace: true }]}><Input maxLength={300} /></Form.Item></Col>
                  <Col xs={24} md={8}><Form.Item name="partenerCui" label="CUI"><Input maxLength={20} /></Form.Item></Col>
                </>
              )}
              <Col xs={12} md={6}><Form.Item name="numarExtern" label={<>Nr. contract<Certitudine c={fisa.numarExtern} /></>}><Input maxLength={50} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item name="dataSemnarii" label={<>Data<Certitudine c={fisa.dataSemnarii} /></>} rules={[{ required: true, message: 'Data contractului?' }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
              <Col xs={12} md={6}><Form.Item name="valoare" label={<>Valoare fără TVA<Certitudine c={fisa.valoare} /></>} rules={[{ required: true }]}><NumarRo min={0} style={{ width: '100%' }} /></Form.Item></Col>
              <Col xs={6} md={3}><Form.Item name="moneda" label="Moneda"><Select options={['EUR', 'RON'].map((m) => ({ value: m }))} /></Form.Item></Col>
              <Col xs={6} md={3}><Form.Item name="tip" label="Tip"><Select popupMatchSelectWidth={false} options={Object.entries(tipuriContract).map(([value, label]) => ({ value, label }))} /></Form.Item></Col>
              <Col xs={24}><Form.Item name="obiect" label={<>Obiectul<Certitudine c={fisa.obiect} /></>} rules={[{ required: true, whitespace: true }]}><Input.TextArea autoSize={{ minRows: 1, maxRows: 3 }} maxLength={1000} /></Form.Item></Col>
            </Row>
            <Typography.Text strong>Asocierea (antreprenorii) — bifează membrul care e firma din aplicație</Typography.Text>
            <Form.Item name="firmaActiva" noStyle>
              <Radio.Group style={{ width: '100%', marginTop: 8 }}>
                <Form.List name="membri">
                  {(campuri, { add, remove }) => (
                    <>
                      {campuri.map(({ key, name }) => (
                        <Row key={key} gutter={6} align="middle" wrap={false}>
                          <Col flex="24px"><Radio value={name} /></Col>
                          <Col flex="auto"><Form.Item name={[name, 'denumire']} style={{ marginBottom: 6 }} rules={[{ required: true, whitespace: true }]}><Input placeholder="Denumire" /></Form.Item></Col>
                          <Col flex="140px"><Form.Item name={[name, 'cui']} style={{ marginBottom: 6 }}><Input placeholder="CUI" /></Form.Item></Col>
                          <Col flex="100px"><Form.Item name={[name, 'cota']} style={{ marginBottom: 6 }} rules={[{ required: true, message: '%?' }]}><NumarRo min={0} max={100} placeholder="%" style={{ width: '100%' }} /></Form.Item></Col>
                          <Col flex="32px"><Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} /></Col>
                        </Row>
                      ))}
                      <Button size="small" type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>Membru</Button>
                    </>
                  )}
                </Form.List>
              </Radio.Group>
            </Form.Item>
          </Form>

          <Tabs size="small" items={[
            { key: 'conditii', label: 'Condiții', children: (
              <Space orientation="vertical">
                <Typography.Text>Perioada: {formatData(fisa.dataInceput)} – {formatData(fisa.dataSfarsit)}{fisa.conditii.durata ? ` · durata: ${fisa.conditii.durata}` : ''} · plata la {fisa.termenPlataZile ?? '—'} zile</Typography.Text>
                <Typography.Text>Avans: {fisa.conditii.transeAvans.length ? fisa.conditii.transeAvans.map((t) => pct(t.procent)).join(' + ') : '—'} · garanție bună execuție {pct(fisa.procentGarantie)} · avans {pct(fisa.conditii.procentGarantieAvans)} · tehnică {pct(fisa.conditii.procentGarantieTehnica)}{fisa.conditii.luniGarantieTehnica ? ` / ${fisa.conditii.luniGarantieTehnica} luni` : ''}</Typography.Text>
                <Typography.Text>Penalități: execuție {pct(fisa.conditii.penalitateExecutieZi)}/zi (plafon {pct(fisa.conditii.plafonPenalitateExecutie)}), plată {pct(fisa.conditii.penalitatePlataZi)}/zi (plafon {pct(fisa.conditii.plafonPenalitatePlata)})</Typography.Text>
                {fisa.conditii.tipPret && <Typography.Text>Tip preț: {fisa.conditii.tipPret}</Typography.Text>}
                {fisa.conditii.stareText && <Typography.Text>Stadiu: {fisa.conditii.stareText}</Typography.Text>}
                {fisa.observatii && <Typography.Paragraph type="secondary" style={{ whiteSpace: 'pre-line', marginBottom: 0 }}>Observații preluate: {fisa.observatii}</Typography.Paragraph>}
              </Space>
            ) },
            { key: 'acte', label: `Acte adiționale (${fisa.acte.length})`, children: (
              <Table size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.acte} columns={[
                { title: 'Nr.', dataIndex: 'numarExtern', width: 60 }, { title: 'Data', dataIndex: 'data', width: 100, render: formatData }, { title: 'Document', dataIndex: 'obiect' },
                { title: 'Modificare', dataIndex: 'diferenta', width: 140, align: 'right', render: lei },
                { title: 'Statut', dataIndex: 'semnat', width: 120, render: (v: boolean) => (v ? 'semnat' : <Tag color="gold">nesemnat</Tag>) },
              ]} />
            ) },
            { key: 'facturi', label: `Facturi (${fisa.facturi.length}) și plăți (${fisa.plati.length})`, children: (
              <Space orientation="vertical" style={{ width: '100%' }}>
                <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.facturi} scroll={{ x: 1200 }} columns={[
                  { title: 'Antreprenor', dataIndex: 'membru', width: 170, ellipsis: true }, { title: 'Factura', dataIndex: 'numar', width: 120 },
                  { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
                  { title: 'Tip', width: 90, render: (_, f) => `${tipuriFacturaContract[f.tip]}${f.transaAvans ? ` T${f.transaAvans}` : ''}` },
                  { title: 'Valoare (EUR)', dataIndex: 'valoare', width: 120, align: 'right', render: (v: number) => (v ? lei(v) : <Tag color="orange">de completat</Tag>) },
                  { title: 'Storno', dataIndex: 'stornoAvans', width: 110, align: 'right', render: (v: number) => (v ? lei(v) : '') },
                  { title: 'Curs', dataIndex: 'curs', width: 70, align: 'right' }, { title: 'Net RON', dataIndex: 'netRon', width: 120, align: 'right', render: lei },
                  { title: 'Total RON', dataIndex: 'totalRon', width: 120, align: 'right', render: lei }, { title: 'Observații', dataIndex: 'observatii', ellipsis: true },
                ]} />
                <Table size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.plati} columns={[
                  { title: 'Factura', dataIndex: 'numarFactura', width: 130 }, { title: 'Data', width: 100, render: (_, p) => formatData(p.plata.data) },
                  { title: 'Instrument', width: 130, render: (_, p) => instrumentePlata[p.plata.instrument] }, { title: 'Sumă RON', width: 130, align: 'right', render: (_, p) => lei(p.plata.sumaRon) },
                  { title: 'Stare', width: 140, render: (_, p) => (p.plata.confirmata ? <Tag color="green">confirmată</Tag> : <Tag color="gold">emisă</Tag>) },
                ]} />
              </Space>
            ) },
            { key: 'obligatii', label: `Obligații (${fisa.obligatii.length})`, children: (
              <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.obligatii} columns={[
                { title: 'Obligație', dataIndex: 'denumire', width: 240 }, { title: 'Temei', dataIndex: 'temei', width: 110 }, { title: 'Termen', dataIndex: 'termen' },
                { title: 'Stare', dataIndex: 'stare', width: 120 }, { title: 'Observații', dataIndex: 'observatii' },
              ]} />
            ) },
            { key: 'garantii', label: `Garanții (${fisa.instrumente.length}) / aspecte (${fisa.riscuri.length})`, children: (
              <Space orientation="vertical" style={{ width: '100%' }}>
                <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.instrumente} columns={[
                  { title: 'Tip', dataIndex: 'tip', width: 160, render: (v: keyof typeof tipuriInstrument) => tipuriInstrument[v] }, { title: 'Nr.', dataIndex: 'numar', width: 130 },
                  { title: 'Emitent', dataIndex: 'emitent', width: 200 }, { title: 'Beneficiar', dataIndex: 'beneficiar', width: 180 },
                  { title: 'Valoare', width: 150, align: 'right', render: (_, i) => `${lei(i.valoare)} ${i.moneda ?? ''}` }, { title: 'Expiră', dataIndex: 'dataExpirare', width: 100, render: formatData },
                ]} />
                <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.riscuri} columns={[
                  { title: 'Element', dataIndex: 'element', width: 240 }, { title: 'Prevedere / valoare', dataIndex: 'prevedere' }, { title: 'Status', dataIndex: 'stare' },
                ]} />
              </Space>
            ) },
            { key: 'actiuni', label: `Acțiuni (${fisa.actiuni.length})`, children: (
              <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.actiuni} columns={[
                { title: 'Acțiune', dataIndex: 'descriere' },
                { title: 'Prioritate', dataIndex: 'prioritate', width: 100, render: (v: keyof typeof prioritati) => <Tag color={prioritati[v].culoare}>{prioritati[v].eticheta}</Tag> },
                { title: 'Stare', dataIndex: 'stare', width: 100, render: (v: keyof typeof stariActiune) => stariActiune[v].eticheta }, { title: 'Observații', dataIndex: 'observatii' },
              ]} />
            ) },
            { key: 'surse', label: `Surse (${fisa.surse.length})`, children: (
              <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.surse} columns={[
                { title: 'Document', dataIndex: 'document' }, { title: 'Data', dataIndex: 'data', width: 100, render: formatData }, { title: 'Observații', dataIndex: 'observatii' },
              ]} />
            ) },
            { key: 'faze', label: `Termene (${fisa.faze.length})`, children: (
              <Table className="tabel-compact" size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={fisa.faze} columns={[
                { title: 'Fază', dataIndex: 'denumire' }, { title: 'Start', dataIndex: 'start', width: 100, render: formatData }, { title: 'Final', dataIndex: 'final', width: 100, render: formatData },
                { title: 'Condiții', dataIndex: 'conditii' }, { title: 'Stare', dataIndex: 'stare' },
              ]} />
            ) },
          ]} />
          <Typography.Text type="secondary">Contractul se creează ca ciornă: se modifică liber, apoi „Activează”. Fișierul original se atașează la contract.</Typography.Text>
        </Space>
      )}
    </Modal>
  )
}
