import { DownloadOutlined, FilePdfOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Form, Input, Segmented, Select, Space, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { useJurnal } from '../../api/audit'
import type { CategorieJurnal, FiltreJurnal, IntrareJurnal } from '../../api/audit'
import { AntetPagina } from '../../layout/AntetPagina'
import { deschidePdf } from '../../util/pdf'

interface ValoriFiltre {
  perioada?: [Dayjs | null, Dayjs | null]
  utilizator?: string
  document?: string
  actiune?: IntrareJurnal['actiune']
}

const culori: Record<string, string> = { Facturi: 'geekblue', NIR: 'purple', Stocuri: 'cyan', Aprovizionare: 'gold' }
const operatii: Record<IntrareJurnal['actiune'], { text: string; culoare: string }> = {
  Adaugat: { text: 'adăugare', culoare: 'green' },
  Modificat: { text: 'modificare', culoare: 'orange' },
  Sters: { text: 'ștergere', culoare: 'red' },
}

function csv(lista: IntrareJurnal[]) {
  const camp = (v: string | null) => (v === null ? '' : /[;"\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)
  const randuri = lista.map((i) => [dayjs(i.moment).format('DD.MM.YYYY HH:mm:ss'), i.utilizator, i.categorie, i.entitate, i.document, operatii[i.actiune]?.text ?? i.actiune, i.descriere].map(camp).join(';'))
  const blob = new Blob(['﻿' + [['Data, ora', 'Utilizator', 'Categorie', 'Ce', 'Document', 'Operație', 'Modificări'].join(';'), ...randuri].join('\r\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `jurnal-modificari-${dayjs().format('YYYY-MM-DD')}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

/**
 * Jurnalul modificărilor: tot ce s-a adăugat, modificat sau șters în facturi, NIR-uri, stocuri și aprovizionare — cine, când,
 * ce document și valorile vechi / noi. Pe categorie sau toate laolaltă, cu raport PDF și export CSV.
 */
export function JurnalModificari() {
  const { message } = App.useApp()
  const [parametri] = useSearchParams()
  const documentUrl = parametri.get('document') ?? undefined
  const [categorie, setCategorie] = useState<CategorieJurnal | 'Toate'>('Toate')
  const [filtre, setFiltre] = useState<Omit<FiltreJurnal, 'categorie'>>(
    documentUrl ? { document: documentUrl } : { dataDe: dayjs().subtract(30, 'day').format('YYYY-MM-DD') })
  const toate = { ...filtre, categorie: categorie === 'Toate' ? undefined : categorie }
  const jurnal = useJurnal(toate)
  const [seGenereaza, setSeGenereaza] = useState(false)

  const descriere = () => {
    const p: string[] = []
    if (filtre.dataDe || filtre.dataPanaLa) p.push(`perioada ${filtre.dataDe ? dayjs(filtre.dataDe).format('DD.MM.YYYY') : '…'} – ${filtre.dataPanaLa ? dayjs(filtre.dataPanaLa).format('DD.MM.YYYY') : '…'}`)
    if (filtre.utilizator) p.push(`utilizatorul „${filtre.utilizator}”`)
    if (filtre.document) p.push(`documentul „${filtre.document}”`)
    if (filtre.actiune) p.push(operatii[filtre.actiune].text)
    return p.join(', ')
  }

  const raport = async () => {
    setSeGenereaza(true)
    try {
      await deschidePdf('/api/audit/jurnal/raport/pdf', { filtre: toate, descriereFiltre: descriere() })
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeGenereaza(false)
    }
  }

  const lista = jurnal.data ?? []
  return (
    <>
      <AntetPagina
        titlu="Jurnal modificări"
        subtitlu="Fiecare adăugare, modificare sau ștergere din facturi, NIR-uri, stocuri și aprovizionare: cine, când, ce document și ce s-a schimbat. Jurnalul nu se poate modifica."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Jurnal modificări' }]}
      />
      <Space orientation="vertical" style={{ width: '100%' }} size="middle">
        <Card size="small">
          <Space orientation="vertical" style={{ width: '100%' }}>
            <Segmented<CategorieJurnal | 'Toate'> value={categorie} onChange={setCategorie}
              options={[{ value: 'Toate', label: 'Toate laolaltă' }, { value: 'Facturi', label: 'Facturi' }, { value: 'NIR', label: 'NIR' }, { value: 'Stocuri', label: 'Stocuri' }, { value: 'Aprovizionare', label: 'Aprovizionare' }]} />
            <Form<ValoriFiltre> layout="inline" style={{ rowGap: 8 }} name="filtreJurnal"
              initialValues={documentUrl ? { document: documentUrl } : { perioada: [dayjs().subtract(30, 'day'), null] }}
              onFinish={(v) => setFiltre({
                dataDe: v.perioada?.[0]?.format('YYYY-MM-DD'), dataPanaLa: v.perioada?.[1]?.format('YYYY-MM-DD'),
                utilizator: v.utilizator?.trim() || undefined, document: v.document?.trim() || undefined, actiune: v.actiune,
              })}>
              <Form.Item name="perioada">
                <DatePicker.RangePicker format="DD.MM.YYYY" allowEmpty={[true, true]}
                  presets={[
                    { label: 'Azi', value: [dayjs(), dayjs()] },
                    { label: 'Ultimele 7 zile', value: [dayjs().subtract(7, 'day'), dayjs()] },
                    { label: 'Luna curentă', value: [dayjs().startOf('month'), dayjs()] },
                    { label: 'Anul curent', value: [dayjs().startOf('year'), dayjs()] },
                  ]} />
              </Form.Item>
              <Form.Item name="document"><Input allowClear placeholder="Document (nr. factură, NIR…)" style={{ width: 210 }} maxLength={300} /></Form.Item>
              <Form.Item name="utilizator"><Input allowClear placeholder="Utilizator" style={{ width: 160 }} maxLength={200} /></Form.Item>
              <Form.Item name="actiune">
                <Select allowClear placeholder="Orice operație" style={{ width: 150 }}
                  options={[{ value: 'Adaugat', label: 'Adăugări' }, { value: 'Modificat', label: 'Modificări' }, { value: 'Sters', label: 'Ștergeri' }]} />
              </Form.Item>
              <Button type="primary" htmlType="submit">Filtrează</Button>
            </Form>
          </Space>
        </Card>

        {jurnal.error && <Alert type="error" showIcon title={(jurnal.error as Error).message} />}

        <Card size="small"
          title={`${lista.length} înregistrări${lista.length >= 5000 ? ' (primele 5000: restrânge filtrele)' : ''}`}
          extra={
            <Space>
              <Button icon={<FilePdfOutlined />} loading={seGenereaza} disabled={lista.length === 0} onClick={() => void raport()}>Raport PDF</Button>
              <Button icon={<DownloadOutlined />} disabled={lista.length === 0} onClick={() => csv(lista)}>Excel (CSV)</Button>
            </Space>
          }>
          <Table<IntrareJurnal>
            rowKey="id" size="small" className="tabel-compact" tableLayout="fixed" scroll={{ x: 1150 }}
            loading={jurnal.isLoading} dataSource={lista}
            pagination={{ defaultPageSize: 50, showSizeChanger: true, pageSizeOptions: [50, 100, 200], hideOnSinglePage: true, size: 'small' }}
            columns={[
              { title: 'Data, ora', dataIndex: 'moment', width: 130, render: (m: string) => dayjs(m).format('DD.MM.YYYY HH:mm') },
              { title: 'Utilizator', dataIndex: 'utilizator', width: 140, ellipsis: { showTitle: true } },
              { title: 'Categorie', dataIndex: 'categorie', width: 105, render: (c: string) => <Tag color={culori[c]}>{c}</Tag> },
              { title: 'Ce', dataIndex: 'entitate', width: 150, ellipsis: { showTitle: true } },
              { title: 'Document', dataIndex: 'document', width: 210, ellipsis: { showTitle: true } },
              { title: 'Operație', dataIndex: 'actiune', width: 100, render: (a: IntrareJurnal['actiune']) => <Tag color={operatii[a]?.culoare}>{operatii[a]?.text ?? a}</Tag> },
              { title: 'Modificări', dataIndex: 'descriere', render: (d: string) => <Typography.Text style={{ whiteSpace: 'normal' }}>{d}</Typography.Text> },
            ]}
          />
        </Card>
      </Space>
    </>
  )
}
