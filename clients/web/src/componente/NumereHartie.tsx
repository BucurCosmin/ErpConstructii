import { DownOutlined, EditOutlined, FilePdfOutlined, NumberOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Collapse, Drawer, Dropdown, Empty, Form, Input, Modal, Space, Table, Tag, Tooltip, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { formatData } from '../util/format'
import { deschidePdf } from '../util/pdf'

// D-48: numerele de pe hârtie ale documentelor importate (necesare de materiale, bonuri de consum). Numărul de sistem
// (NM/AAAA/NNNN, BC/AAAA/NNNN) se dă automat și nu se schimbă; numărul de hârtie se verifică pe ani și se renumerotează cu motiv.

export interface DocumentCuNumarExtern {
  id: string
  proiectId: string
  numarSistem: string
  numarExtern: string | null
  data: string
  proiect: string
}

/** O serie de numere de hârtie: proiectul și anul datei documentului (fiecare lucrare numerotează de la capăt). */
export interface VerificareNumereAn {
  an: number
  proiectId: string
  proiect: string
  documente: number
  primul: number | null
  ultimul: number | null
  suprapuneri: { numar: number; documente: DocumentCuNumarExtern[] }[]
  lipsa: string[]
  numereLipsa: number
  faraNumar: DocumentCuNumarExtern[]
  urmatorulLiber: number
}

export interface Renumerotare {
  id: string
  documentId: string
  documentNumar: string
  numarVechi: string | null
  numarNou: string
  motiv: string
  utilizator: string
  la: string
}

/** Documentul de renumerotat: numărul de sistem, cel de hârtie, data (anul seriei) și proiectul (din ruta cererii). */
export interface DocumentDeRenumerotat {
  id: string
  proiectId: string
  numarSistem: string
  numarExtern: string | null
  data: string
}

/** Unde stau endpointurile unui tip de document (necesare / bonuri de consum). */
export interface SerieNumere {
  /** Ex. „necesarele importate”, „bonurile de consum”. */
  denumire: string
  verificare: string
  istoric: string
  renumerotare: (d: DocumentDeRenumerotat) => string
  /** Cheile interogărilor de reîncărcat după renumerotare (listele documentelor). */
  invalideaza: readonly (readonly unknown[])[]
}

const cheie = (s: SerieNumere) => ['numere-hartie', s.verificare] as const

function useRenumeroteaza(serie: SerieNumere) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ document, numarNou, motiv }: { document: DocumentDeRenumerotat; numarNou: string; motiv: string }) =>
      api.post<unknown>(serie.renumerotare(document), { numarNou, motiv }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: cheie(serie) })
      for (const k of serie.invalideaza) void qc.invalidateQueries({ queryKey: k })
    },
  })
}

/** Fereastra de renumerotare: numărul nou (propus: primul liber din an, dacă se știe) și motivul. */
export function ModalRenumerotare({ serie, document, propus, onInchide }: {
  serie: SerieNumere
  document?: DocumentDeRenumerotat
  propus?: number
  onInchide: () => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<{ numarNou: string; motiv: string }>()
  const renumeroteaza = useRenumeroteaza(serie)

  useEffect(() => {
    if (document) form.setFieldsValue({ numarNou: propus ? String(propus) : (document.numarExtern ?? ''), motiv: '' })
  }, [document, propus, form])

  const salveaza = async () => {
    if (!document) return
    const v = await form.validateFields()
    try {
      await renumeroteaza.mutateAsync({ document, numarNou: v.numarNou.trim(), motiv: v.motiv.trim() })
      message.success(`${document.numarSistem}: numărul de pe hârtie este acum ${v.numarNou.trim()}.`)
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal open={!!document} title={document ? `Renumerotare ${document.numarSistem}` : ''} okText="Renumerotează" cancelText="Renunță"
      onOk={() => void salveaza()} onCancel={onInchide} confirmLoading={renumeroteaza.isPending} destroyOnHidden>
      {document && (
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Typography.Text type="secondary">
            Se schimbă doar numărul de pe hârtie (din {formatData(document.data)}); numărul de sistem {document.numarSistem} rămâne.
            Schimbarea intră în istoric, cu motivul.
          </Typography.Text>
          <Form form={form} layout="vertical">
            <Form.Item label="Numărul actual">
              <Input value={document.numarExtern ?? '— (fără număr)'} disabled />
            </Form.Item>
            <Form.Item name="numarNou" label="Numărul nou" rules={[{ required: true, whitespace: true, max: 50 }]}
              extra={propus ? `Propus: ${propus}, primul număr liber pe proiect în ${document.data.slice(0, 4)}.` : 'Numărul trebuie să fie liber pe proiect, în anul documentului.'}>
              <Input autoFocus />
            </Form.Item>
            <Form.Item name="motiv" label="Motiv" rules={[{ required: true, whitespace: true, max: 500 }]}>
              <Input placeholder="Ex. număr dublat cu alt document, document fără număr" />
            </Form.Item>
          </Form>
        </Space>
      )}
    </Modal>
  )
}

/** Butonul „Verifică numerele” cu fereastra lui: suprapuneri, numere sărite, documente fără număr, istoric. */
export function VerificareNumere({ serie, poateEdita }: { serie: SerieNumere; poateEdita: boolean }) {
  const [deschis, setDeschis] = useState(false)
  const [deRenumerotat, setDeRenumerotat] = useState<{ document: DocumentDeRenumerotat; propus?: number }>()
  const verificare = useQuery({ queryKey: cheie(serie), queryFn: () => api.get<VerificareNumereAn[]>(serie.verificare), enabled: deschis })
  const istoric = useQuery({ queryKey: [...cheie(serie), 'istoric'], queryFn: () => api.get<Renumerotare[]>(serie.istoric), enabled: deschis })
  const probleme = (verificare.data ?? []).reduce((s, a) => s + a.suprapuneri.length + a.faraNumar.length, 0)

  const coloane = (an: VerificareNumereAn) => [
    { title: 'Document', dataIndex: 'numarSistem', width: 150 },
    { title: 'Nr. hârtie', dataIndex: 'numarExtern', width: 110, render: (v: string | null) => v ?? '—' },
    { title: 'Data', dataIndex: 'data', width: 110, render: formatData },
    { title: 'Proiect', dataIndex: 'proiect', ellipsis: true },
    ...(poateEdita ? [{
      title: '', key: 'r', width: 60,
      render: (_: unknown, d: DocumentCuNumarExtern) => (
        <Tooltip title="Renumerotează">
          <Button size="small" icon={<EditOutlined />} onClick={() => setDeRenumerotat({ document: d, propus: an.urmatorulLiber })} />
        </Tooltip>
      ),
    }] : []),
  ]

  return (
    <>
      <Button icon={<NumberOutlined />} onClick={() => setDeschis(true)}>Verifică numerele</Button>
      <Drawer open={deschis} onClose={() => setDeschis(false)} size={820} title={`Numerele de pe hârtie — ${serie.denumire}`}>
        <Space orientation="vertical" style={{ width: '100%' }} size="middle">
          <Typography.Text type="secondary">
            Fiecare document are un număr de sistem dat automat (care nu se schimbă) și numărul de pe hârtie. Numerele de pe hârtie
            se verifică pe proiect și pe an (anul datei documentului): același număr pe proiecte diferite e în regulă; pe același
            proiect, documentul în plus se renumerotează, iar documentele fără număr primesc unul liber.
          </Typography.Text>
          {verificare.error && <Alert type="error" showIcon title={verificare.error.message} />}
          {verificare.data?.length === 0 && <Empty description="Nu există documente cu număr de hârtie." />}
          {verificare.data && verificare.data.length > 0 && (
            <Alert type={probleme ? 'warning' : 'success'} showIcon
              title={probleme ? `${probleme} probleme de numerotare (suprapuneri sau documente fără număr).` : 'Numerele de pe hârtie nu se suprapun.'} />
          )}
          {verificare.data?.map((an) => (
            <div key={`${an.an}-${an.proiectId}`}>
              <Typography.Title level={5} style={{ marginTop: 0 }}>
                {an.proiect} · {an.an}{' '}
                <Typography.Text type="secondary" style={{ fontWeight: 'normal', fontSize: 14 }}>
                  {an.documente} documente{an.primul != null ? `, numerele ${an.primul}–${an.ultimul}` : ''} · următorul liber: {an.urmatorulLiber}
                </Typography.Text>
              </Typography.Title>
              {an.suprapuneri.map((s) => (
                <div key={s.numar} style={{ marginBottom: 12 }}>
                  <Tag color="red">nr. {s.numar} pe {s.documente.length} documente</Tag>
                  <Table<DocumentCuNumarExtern> size="small" rowKey="id" pagination={false} dataSource={s.documente} columns={coloane(an)} style={{ marginTop: 6 }} />
                </div>
              ))}
              {an.faraNumar.length > 0 && (
                <div style={{ marginBottom: 12 }}>
                  <Tag color="orange">{an.faraNumar.length} fără număr de hârtie</Tag>
                  <Table<DocumentCuNumarExtern> size="small" rowKey="id" pagination={false} dataSource={an.faraNumar} columns={coloane(an)} style={{ marginTop: 6 }} />
                </div>
              )}
              {an.numereLipsa > 0 && (
                <Typography.Paragraph type="secondary">
                  Numere sărite ({an.numereLipsa}): {an.lipsa.join(', ')}{an.lipsa.length < 60 ? '' : ' …'}. Pot fi documente încă neimportate.
                </Typography.Paragraph>
              )}
              {an.suprapuneri.length === 0 && an.faraNumar.length === 0 && <Typography.Paragraph type="success">Fără suprapuneri.</Typography.Paragraph>}
            </div>
          ))}
          <Collapse size="small" items={[{
            key: 'istoric',
            label: `Istoricul renumerotărilor (${istoric.data?.length ?? 0})`,
            children: (
              <Table<Renumerotare> size="small" rowKey="id" pagination={{ pageSize: 10, hideOnSinglePage: true }} dataSource={istoric.data}
                columns={[
                  { title: 'Document', dataIndex: 'documentNumar', width: 140 },
                  { title: 'Nr. vechi → nou', key: 'n', width: 130, render: (_, r) => `${r.numarVechi ?? '—'} → ${r.numarNou}` },
                  { title: 'Motiv', dataIndex: 'motiv' },
                  { title: 'De', key: 'de', width: 180, render: (_, r) => `${r.utilizator}, ${new Date(r.la).toLocaleString('ro-RO')}` },
                ]} />
            ),
          }]} />
        </Space>
      </Drawer>
      <ModalRenumerotare serie={serie} document={deRenumerotat?.document} propus={deRenumerotat?.propus} onInchide={() => setDeRenumerotat(undefined)} />
    </>
  )
}

const TIPURI_RAPORT = [
  { key: 'registru', label: 'Registru (un rând pe document)' },
  { key: 'detaliat', label: 'Detaliat (documentele cu materialele lor)' },
  { key: 'materiale', label: 'Materiale adunate pe lucrare' },
]

/** Rapoartele PDF ale documentelor afișate (cele filtrate): implicit registru, detaliat pe materiale, materiale adunate pe lucrare. */
export function ButonRaport({ url, ids, filtre, csv, tipuri = TIPURI_RAPORT }: {
  url: string
  ids: string[]
  filtre: string
  /** Exportul CSV al materialelor (opțional). */
  csv?: () => void
  /** Tipurile de raport ale serverului (cheia = `tip` din cerere). */
  tipuri?: { key: string; label: string }[]
}) {
  const { message } = App.useApp()
  const [seGenereaza, setSeGenereaza] = useState(false)
  const raport = async (tip: string) => {
    setSeGenereaza(true)
    try {
      await deschidePdf(url, { id: ids, tip, filtre })
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeGenereaza(false)
    }
  }

  return (
    <Dropdown disabled={ids.length === 0} menu={{
      items: [
        ...tipuri,
        ...(csv ? [{ type: 'divider' as const }, { key: 'csv', label: 'Export CSV (materialele din documente)' }] : []),
      ],
      onClick: ({ key }) => (key === 'csv' ? csv?.() : void raport(key)),
    }}>
      <Button icon={<FilePdfOutlined />} loading={seGenereaza}>
        Raport ({ids.length}) <DownOutlined />
      </Button>
    </Dropdown>
  )
}
