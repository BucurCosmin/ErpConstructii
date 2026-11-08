import { CheckOutlined, CloseCircleOutlined, DeleteOutlined, EditOutlined, FileAddOutlined, FlagOutlined, PaperClipOutlined, UploadOutlined } from '@ant-design/icons'
import {
  Alert, App, Button, Card, Col, DatePicker, Descriptions, Drawer, Empty, Flex, Form, Input, Modal, Popconfirm, Progress, Row, Select, Space, Spin,
  Statistic, Table, Tag, Typography, Upload,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  stariContract, tipuriContract, useActiveazaContract, useAdaugaActAditional, useContract, useIncarcaDocumentContract, useInchideContract, useStergeContract,
} from '../../api/contracte'
import type { Contract } from '../../api/contracte'
import { api } from '../../api/client'
import { LinkFactura } from '../../componente/LinkFactura'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatData, formatLei } from '../../util/format'
import { EditorContract } from './EditorContract'

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
          {c.stare === 'Ciorna' && <Alert type="info" showIcon title="Ciornă: se modifică liber. După semnare apasă „Activează”; abia atunci se pot lega facturi." />}
          {c.inchidere && (
            <Alert type={c.inchidere.tip === 'Reziliat' ? 'error' : 'success'} showIcon
              title={`${c.inchidere.tip === 'Reziliat' ? 'Reziliat' : 'Finalizat'} la ${formatData(c.inchidere.data)} de ${c.inchidere.inchisDe}`}
              description={c.inchidere.motiv ?? 'Nu se mai leagă facturi noi de el.'} />
          )}
          {c.situatie.depasit && <Alert type="error" showIcon title={`Facturat peste valoarea contractului cu ${formatLei(-c.situatie.rest)} ${c.moneda}: e nevoie de un act adițional.`} />}
          {c.situatie.termenDepasit && <Alert type="warning" showIcon title={`Termenul de finalizare (${formatData(c.situatie.termen)}) a trecut.`} />}

          <Card size="small">
            <Row gutter={16}>
              <Col xs={12} md={5}><Statistic title="Valoare actualizată" value={c.situatie.valoareActualizata} formatter={(v) => formatLei(Number(v))} suffix={c.moneda} /></Col>
              <Col xs={12} md={5}><Statistic title="Facturat" value={c.situatie.facturat} formatter={(v) => formatLei(Number(v))} /></Col>
              <Col xs={12} md={5}><Statistic title="Rest de facturat" value={c.situatie.rest} formatter={(v) => formatLei(Number(v))} valueStyle={c.situatie.rest < 0 ? { color: '#cf1322' } : undefined} /></Col>
              <Col xs={12} md={5}><Statistic title={`Garanție reținută (${(c.procentGarantie * 100).toLocaleString('ro-RO')}%)`} value={c.situatie.garantieRetinuta} formatter={(v) => formatLei(Number(v))} /></Col>
              <Col xs={24} md={4}><Typography.Text type="secondary">Facturat din contract</Typography.Text><ProgresContract c={c} /></Col>
            </Row>
          </Card>

          <Descriptions size="small" bordered column={{ xs: 1, md: 2 }} items={[
            { label: 'Proiect', children: c.proiectCod },
            { label: 'Tip', children: tipuriContract[c.tip] },
            { label: 'Partener', children: `${c.partenerDenumire}${c.partenerCui ? ` (CUI ${c.partenerCui})` : ''}` },
            { label: 'Data contractului', children: formatData(c.dataSemnarii) },
            { label: 'Obiect', children: c.obiect, span: 2 },
            { label: 'Valoare inițială', children: `${formatLei(c.valoare)} ${c.moneda} fără TVA${c.linii.length > 0 ? ' (pe poziții)' : ''}` },
            { label: 'Perioada', children: `${formatData(c.dataInceput)} – ${formatData(c.situatie.termen)}${c.situatie.termen !== c.dataSfarsit ? ' (prelungit)' : ''}` },
            { label: 'Plata', children: c.termenPlataZile !== null ? `la ${c.termenPlataZile} zile` : '—' },
            { label: 'Avans', children: c.avans > 0 ? `${formatLei(c.avans)} ${c.moneda}` : '—' },
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

          <Table size="small" pagination={false} rowKey="id" dataSource={c.acte ?? []} title={() => <Typography.Text strong>Acte adiționale</Typography.Text>}
            locale={{ emptyText: 'Niciun act adițional.' }}
            columns={[
              { title: 'Nr.', width: 60, render: (_, a) => a.numar },
              { title: 'Data', dataIndex: 'data', width: 105, render: formatData },
              { title: 'Obiect', dataIndex: 'obiect' },
              { title: 'Valoare', dataIndex: 'diferentaValoare', width: 140, align: 'right', render: (v: number) => (v === 0 ? '—' : `${v > 0 ? '+' : ''}${formatLei(v)}`) },
              { title: 'Termen nou', dataIndex: 'dataSfarsitNoua', width: 110, render: formatData },
            ]} />

          <Table size="small" pagination={false} rowKey="id" dataSource={c.facturi ?? []} title={() => <Typography.Text strong>Facturi legate</Typography.Text>}
            locale={{ emptyText: deschis ? 'Nicio factură încă: facturile partenerului se leagă de contract la import.' : 'Nicio factură.' }}
            columns={[
              { title: 'Factura', render: (_, f) => <LinkFactura id={f.id} numar={f.numar} /> },
              { title: 'Data', dataIndex: 'data', width: 110, render: formatData },
              { title: 'Fără TVA', width: 150, align: 'right', render: (_, f) => `${formatLei(f.totalFaraTva)} ${f.moneda}` },
              { title: 'Garanție reținută', width: 150, align: 'right', render: (_, f) => formatLei(Math.round(f.totalFaraTva * c.procentGarantie * 100) / 100) },
            ]} />

          <Card size="small" title="Documente (contractul semnat, acte adiționale, anexe)"
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
  const [form] = Form.useForm<{ numarExtern?: string; data: Dayjs; obiect: string; diferentaValoare?: number | null; dataSfarsitNoua?: Dayjs | null }>()
  return (
    <Modal open={deschis} onCancel={onInchide} title={`Act adițional la ${contract.numar}`} okText="Adaugă" cancelText="Renunță" onOk={() => form.submit()}
      confirmLoading={adauga.isPending} destroyOnHidden>
      <Form form={form} layout="vertical" preserve={false} initialValues={{ data: dayjs() }} onFinish={async (v) => {
        try {
          await adauga.mutateAsync({ proiectId: contract.proiectId, id: contract.id, date: {
            numarExtern: v.numarExtern?.trim() || null, data: v.data.format('YYYY-MM-DD'), obiect: v.obiect.trim(),
            diferentaValoare: Number(v.diferentaValoare ?? 0), dataSfarsitNoua: v.dataSfarsitNoua?.format('YYYY-MM-DD') ?? null,
          } })
          message.success('Actul adițional a fost adăugat.')
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
