import { CheckOutlined, CloseOutlined, DownloadOutlined, FilePdfOutlined, FileWordOutlined, PaperClipOutlined, PlusOutlined, PrinterOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, App, Badge, Button, Card, DatePicker, Dropdown, Form, Input, Modal, Popconfirm, Segmented, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import { useAcceptaNotaComanda, useNoteComanda, useRespingeNotaComanda } from '../../api/aprovizionare'
import type { FiltreNoteComanda, LinieNotaComanda, NotaComanda, StareNotaComanda } from '../../api/aprovizionare'
import { useProiecte } from '../../api/interogari'
import { formatData } from '../../util/format'
import { deschidePdf } from '../../util/pdf'

interface ValoriFiltre {
  perioada?: [Dayjs | null, Dayjs | null]
  proiectId?: string
  cauta?: string
  furnizor?: string
  material?: string
  lucrare?: string
  sursa?: NotaComanda['sursa']
}

function descarca(blob: Blob, nume: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nume
  a.click()
  URL.revokeObjectURL(url)
}

/** CSV pentru Excel (separator „;”, BOM pentru diacritice, virgulă zecimală). */
function csv(cap: string[], randuri: (string | number | null)[][], nume: string) {
  const camp = (v: string | number | null) => {
    if (v === null) return ''
    const s = typeof v === 'number' ? v.toString().replace('.', ',') : v
    return /[;"\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  descarca(new Blob(['﻿' + [cap.join(';'), ...randuri.map((r) => r.map(camp).join(';'))].join('\r\n')], { type: 'text/csv;charset=utf-8' }), nume)
}

const numeFisier = (n: NotaComanda) => `${n.numar}_${n.furnizorDenumire}`.replace(/[^a-zA-Z0-9_-]+/g, '_')
const cantitate = (v: number) => v.toLocaleString('ro-RO', { maximumFractionDigits: 4 })

/**
 * Notele de comandă către furnizori (formularul GC 007): filtre (perioadă, lucrare / proiect, număr, furnizor, material, sursă),
 * documentul Word din șablonul firmei, PDF, originalul scanat, rapoarte PDF pe lucrare și export CSV.
 */
export function ListaNoteComanda({ onNotaNoua, poateCrea, poateAproba }: { onNotaNoua?: () => void; poateCrea?: boolean; poateAproba?: boolean }) {
  const { message } = App.useApp()
  const proiecte = useProiecte()
  const [filtre, setFiltre] = useState<FiltreNoteComanda>({})
  // D-37: notele generate din necesare se acceptă aici; „Emise” = comenzile trimise la furnizori (rapoartele le iau doar pe ele).
  const [stare, setStare] = useState<StareNotaComanda>('Emisa')
  const note = useNoteComanda({ ...filtre, stare })
  const deAcceptat = useNoteComanda({ stare: 'DeAcceptat' })
  const accepta = useAcceptaNotaComanda()
  const respingeNota = useRespingeNotaComanda()
  const [deRespins, setDeRespins] = useState<NotaComanda>()
  const [motiv, setMotiv] = useState('')
  const emise = stare === 'Emisa'

  const acceptaNota = async (n: NotaComanda) => {
    try {
      await accepta.mutateAsync({ proiectId: n.proiectId, id: n.id })
      message.success(`${n.numar} a fost acceptată${n.necesarNumar ? ` (necesarul ${n.necesarNumar} e aprobat)` : ''}: a ajuns la achiziții, în „Necesar de achiziție”.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const respinge = async () => {
    if (!deRespins || !motiv.trim()) return
    try {
      await respingeNota.mutateAsync({ proiectId: deRespins.proiectId, id: deRespins.id, motiv: motiv.trim() })
      message.success(`${deRespins.numar} a fost respinsă.`)
      setDeRespins(undefined)
      setMotiv('')
    } catch (e) {
      message.error((e as Error).message)
    }
  }
  const [selectate, setSelectate] = useState<string[]>([])
  const [seGenereaza, setSeGenereaza] = useState(false)

  const lista = note.data ?? []
  const alese = selectate.length ? lista.filter((n) => selectate.includes(n.id)) : lista
  const nr = alese.length

  const descriereFiltre = () => {
    const p: string[] = []
    if (filtre.dataDe || filtre.dataPanaLa) p.push(`perioada ${filtre.dataDe ? formatData(filtre.dataDe) : '…'} – ${filtre.dataPanaLa ? formatData(filtre.dataPanaLa) : '…'}`)
    const pr = proiecte.data?.find((x) => x.id === filtre.proiectId)
    if (pr) p.push(`proiectul ${pr.cod}`)
    if (filtre.lucrare) p.push(`lucrarea „${filtre.lucrare}”`)
    if (filtre.cauta) p.push(`numărul „${filtre.cauta}”`)
    if (filtre.furnizor) p.push(`furnizorul „${filtre.furnizor}”`)
    if (filtre.material) p.push(`materialul „${filtre.material}”`)
    if (filtre.sursa) p.push(filtre.sursa === 'Sistem' ? 'emise în aplicație' : 'importate de pe hârtie')
    if (selectate.length) p.push(`${selectate.length} note bifate`)
    return p.join(', ')
  }

  const pdf = async (url: string, corp?: unknown) => {
    setSeGenereaza(true)
    try {
      await deschidePdf(url, corp)
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeGenereaza(false)
    }
  }

  const word = async (n: NotaComanda) => {
    try {
      descarca(await api.getBinar(`/api/aprovizionare/proiecte/${n.proiectId}/note-comanda/${n.id}/document.docx`), `${numeFisier(n)}.docx`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const original = async (n: NotaComanda) => {
    try {
      const blob = await api.getBinar(`/api/aprovizionare/proiecte/${n.proiectId}/note-comanda/${n.id}/original`)
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank')
      setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const raport = (tip: string) => void pdf('/api/aprovizionare/note-comanda/raport/pdf', { id: alese.map((n) => n.id), tip, filtre: descriereFiltre() })

  const exportNote = () => csv(
    ['Număr', 'Nr. hârtie', 'Data', 'Proiect', 'Obiect / lucrare', 'Furnizor', 'CUI', 'Poziții', 'Sursă', 'Întocmit de'],
    alese.map((n) => [n.numar, n.numarExtern, formatData(n.data), n.proiectCod, n.lucrare, n.furnizorDenumire, n.furnizorCui, n.linii.length,
      n.sursa === 'Sistem' ? 'emisă' : 'importată', n.inregistrataDe]),
    `note-comanda-${dayjs().format('YYYY-MM-DD')}.csv`)
  const exportLinii = () => csv(
    ['Număr', 'Data', 'Proiect', 'Obiect / lucrare', 'Furnizor', 'Material / produs', 'UM', 'Cantitate', 'Termen'],
    alese.flatMap((n) => n.linii.map((l) => [n.numar, formatData(n.data), n.proiectCod, n.lucrare, n.furnizorDenumire, l.denumire, l.unitateMasura, l.cantitate, l.termen])),
    `materiale-note-comanda-${dayjs().format('YYYY-MM-DD')}.csv`)

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Segmented<StareNotaComanda> value={stare} onChange={(v) => { setStare(v); setSelectate([]) }}
        options={[
          { value: 'Emisa', label: 'Emise către furnizori' },
          { value: 'DeAcceptat', label: <Space size={6}>De acceptat<Badge count={deAcceptat.data?.length ?? 0} color="#d4380d" /></Space> },
          { value: 'Acceptata', label: 'Acceptate (la achiziții)' },
          { value: 'Respinsa', label: 'Respinse' },
        ]} />
      <Form<ValoriFiltre>
        layout="inline"
        style={{ rowGap: 8 }}
        name="filtreNoteComanda"
        onFinish={(v) => {
          setSelectate([])
          setFiltre({
            proiectId: v.proiectId, cauta: v.cauta?.trim() || undefined, furnizor: v.furnizor?.trim() || undefined,
            material: v.material?.trim() || undefined, lucrare: v.lucrare?.trim() || undefined, sursa: v.sursa,
            dataDe: v.perioada?.[0]?.format('YYYY-MM-DD'), dataPanaLa: v.perioada?.[1]?.format('YYYY-MM-DD'),
          })
        }}
      >
        <Form.Item name="perioada">
          <DatePicker.RangePicker format="DD.MM.YYYY" allowEmpty={[true, true]}
            presets={[
              { label: 'Luna curentă', value: [dayjs().startOf('month'), dayjs()] },
              { label: 'Luna trecută', value: [dayjs().subtract(1, 'month').startOf('month'), dayjs().subtract(1, 'month').endOf('month')] },
              { label: 'Anul curent', value: [dayjs().startOf('year'), dayjs()] },
              { label: 'Anul trecut', value: [dayjs().subtract(1, 'year').startOf('year'), dayjs().subtract(1, 'year').endOf('year')] },
            ]} />
        </Form.Item>
        <Form.Item name="proiectId">
          <Select allowClear placeholder="Toate proiectele" style={{ width: 220 }} showSearch={{ optionFilterProp: 'label' }}
            options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} — ${p.denumire}` }))} />
        </Form.Item>
        <Form.Item name="lucrare">
          <Input allowClear placeholder="Obiect / lucrare" style={{ width: 160 }} maxLength={300} />
        </Form.Item>
        <Form.Item name="cauta">
          <Input allowClear placeholder="Număr (NC/… sau de pe hârtie)" style={{ width: 200 }} maxLength={50} />
        </Form.Item>
        <Form.Item name="furnizor">
          <Input allowClear placeholder="Furnizor" style={{ width: 160 }} maxLength={300} />
        </Form.Item>
        <Form.Item name="material">
          <Input allowClear prefix={<SearchOutlined />} placeholder="Material comandat" style={{ width: 180 }} maxLength={300} />
        </Form.Item>
        <Form.Item name="sursa">
          <Select allowClear placeholder="Orice sursă" style={{ width: 150 }}
            options={[{ value: 'Sistem', label: 'Emise în aplicație' }, { value: 'Import', label: 'De pe hârtie' }]} />
        </Form.Item>
        <Button type="primary" htmlType="submit">Filtrează</Button>
        {onNotaNoua && (
          <Button style={{ marginInlineStart: 'auto' }} icon={<PlusOutlined />} onClick={onNotaNoua} disabled={!poateCrea}>Notă de comandă nouă</Button>
        )}
      </Form>

      {note.error && <Alert type="error" showIcon title="Notele de comandă nu au putut fi încărcate." description={(note.error as Error).message} />}

      <Card
        size="small"
        title={
          <Space wrap size="large">
            <span>{lista.length} note de comandă</span>
            <Typography.Text type="secondary" style={{ fontWeight: 'normal' }}>
              {new Set(lista.map((n) => n.proiectId)).size} lucrări · {new Set(lista.map((n) => n.furnizorDenumire.toUpperCase())).size} furnizori ·{' '}
              {lista.reduce((s, n) => s + n.linii.length, 0)} poziții
            </Typography.Text>
          </Space>
        }
        extra={
          <Space wrap>
            {selectate.length > 0 && <Tag color="blue" closable onClose={() => setSelectate([])}>{selectate.length} bifate</Tag>}
            {emise && <Dropdown
              disabled={nr === 0}
              menu={{
                items: [
                  { key: 'registru', label: 'Registrul notelor, pe lucrări' },
                  { key: 'detaliat', label: 'Notele cu materialele lor, pe lucrări' },
                  { key: 'materiale', label: 'Materiale comandate pe lucrare (cantități adunate)' },
                ],
                onClick: ({ key }) => raport(key),
              }}
            >
              <Button icon={<FilePdfOutlined />} loading={seGenereaza}>Rapoarte PDF{selectate.length ? ' (bifate)' : ''}</Button>
            </Dropdown>}
            {emise && <Tooltip title="Notele alese, fiecare pe pagina ei, într-un singur PDF">
              <Button icon={<PrinterOutlined />} disabled={nr === 0} loading={seGenereaza}
                onClick={() => (nr > 200 ? message.warning('Se pot tipări cel mult 200 de note deodată: bifează mai puține.') : void pdf('/api/aprovizionare/note-comanda/pdf', { id: alese.map((n) => n.id) }))}>
                Tipărește ({nr})
              </Button>
            </Tooltip>}
            <Dropdown disabled={nr === 0}
              menu={{ items: [{ key: 'n', label: 'Note (o linie pe notă)' }, { key: 'l', label: 'Materiale (o linie pe poziție)' }], onClick: ({ key }) => (key === 'n' ? exportNote() : exportLinii()) }}>
              <Button icon={<DownloadOutlined />}>Excel (CSV)</Button>
            </Dropdown>
          </Space>
        }
      >
        <Table<NotaComanda>
          rowKey="id"
          size="small"
          className="tabel-compact"
          tableLayout="fixed"
          scroll={{ x: 1100 }}
          loading={note.isLoading}
          dataSource={lista}
          rowSelection={{ selectedRowKeys: selectate, onChange: (k) => setSelectate(k as string[]) }}
          pagination={{ defaultPageSize: 50, showSizeChanger: true, pageSizeOptions: [25, 50, 100, 200], hideOnSinglePage: true, size: 'small' }}
          expandable={{
            expandedRowRender: (n) => (
              <div>
                <Table<LinieNotaComanda>
                  rowKey={(_, i) => String(i)}
                  size="small"
                  className="tabel-compact"
                  tableLayout="fixed"
                  pagination={false}
                  dataSource={n.linii}
                  columns={[
                    { title: 'Nr.', width: 45, render: (_, __, i) => i + 1 },
                    { title: 'Material / produs', dataIndex: 'denumire', ellipsis: { showTitle: true } },
                    { title: 'UM', dataIndex: 'unitateMasura', width: 70 },
                    { title: 'Cantitate', dataIndex: 'cantitate', width: 100, className: 'numeric', render: cantitate },
                    { title: 'Termen', dataIndex: 'termen', width: 130 },
                  ]}
                />
                <Space wrap size={16} style={{ marginTop: 6 }}>
                  {(n.furnizorAdresa || n.furnizorTelefon || n.furnizorEmail) && (
                    <Typography.Text type="secondary">Furnizor: {[n.furnizorAdresa, n.furnizorTelefon, n.furnizorEmail].filter(Boolean).join(' · ')}</Typography.Text>
                  )}
                  <Typography.Text type="secondary">Întocmită de {n.inregistrataDe}, {formatData(n.inregistrataLa)}</Typography.Text>
                  {n.acceptataDe && (
                    <Typography.Text type="secondary">{n.stare === 'Respinsa' ? 'Respinsă' : 'Acceptată'} de {n.acceptataDe}{n.acceptataLa ? `, ${formatData(n.acceptataLa)}` : ''}</Typography.Text>
                  )}
                  {n.motivRespingere && <Typography.Text type="danger">Motiv: {n.motivRespingere}</Typography.Text>}
                  {n.observatii && <Typography.Text type="secondary">Observații: {n.observatii}</Typography.Text>}
                </Space>
              </div>
            ),
          }}
          columns={[
            {
              title: 'Număr', dataIndex: 'numar', width: 150, sorter: (a, b) => a.numar.localeCompare(b.numar),
              render: (numar: string, n) => (
                <Tooltip title={n.sursa === 'Import' ? `Importată de pe hârtie${n.numarExtern ? `; nr. ${n.numarExtern} pe document` : ''}` : 'Emisă în aplicație'}>
                  <span>{numar}</span>{n.numarExtern && <Typography.Text type="secondary"> · {n.numarExtern}</Typography.Text>}
                  {n.necesarNumar && <div><Typography.Text type="secondary" style={{ fontSize: 12 }}>din {n.necesarNumar}</Typography.Text></div>}
                </Tooltip>
              ),
            },
            { title: 'Data', dataIndex: 'data', width: 92, render: formatData, sorter: (a, b) => a.data.localeCompare(b.data), defaultSortOrder: 'descend' },
            { title: 'Proiect', dataIndex: 'proiectCod', width: 110, ellipsis: { showTitle: true }, sorter: (a, b) => a.proiectCod.localeCompare(b.proiectCod) },
            { title: 'Obiect / lucrare', dataIndex: 'lucrare', ellipsis: { showTitle: true } },
            {
              title: 'Furnizor', dataIndex: 'furnizorDenumire', ellipsis: { showTitle: true }, sorter: (a, b) => a.furnizorDenumire.localeCompare(b.furnizorDenumire),
              render: (f: string) => f || <Typography.Text type="secondary">— (se alege la achiziții)</Typography.Text>,
            },
            { title: 'Poz.', key: 'linii', width: 55, className: 'numeric', render: (_, n) => n.linii.length },
            {
              title: 'Sursă', dataIndex: 'sursa', width: 105,
              render: (s: NotaComanda['sursa'], n) => {
                if (n.stare === 'DeAcceptat') return <Tag color="gold">de acceptat</Tag>
                if (n.stare === 'Acceptata') return <Tag color="blue">acceptată</Tag>
                if (n.stare === 'Respinsa') return <Tag color="red">respinsă</Tag>
                return s === 'Sistem' ? <Tag color="green">emisă</Tag> : <Tag color="purple">hârtie</Tag>
              },
            },
            {
              title: '', key: 'actiuni', width: stare === 'DeAcceptat' ? 150 : 100, fixed: 'right',
              render: (_, n) => (
                <Space size={2}>
                  {n.stare === 'DeAcceptat' && poateAproba && (
                    <>
                      <Popconfirm title={`Accepți ${n.numar}?`} description="Se aprobă și necesarul; nota ajunge la achiziții." okText="Acceptă" cancelText="Nu"
                        onConfirm={() => acceptaNota(n)}>
                        <Tooltip title="Acceptă (aprobă necesarul; nota ajunge la achiziții)">
                          <Button size="small" type="primary" icon={<CheckOutlined />} loading={accepta.isPending} />
                        </Tooltip>
                      </Popconfirm>
                      <Tooltip title="Respinge (cu motiv)">
                        <Button size="small" danger icon={<CloseOutlined />} onClick={() => setDeRespins(n)} />
                      </Tooltip>
                    </>
                  )}
                  <Tooltip title="Word (șablonul firmei GC 007)">
                    <Button size="small" type="text" icon={<FileWordOutlined />} onClick={() => void word(n)} />
                  </Tooltip>
                  <Tooltip title="PDF">
                    <Button size="small" type="text" icon={<FilePdfOutlined />} onClick={() => void pdf(`/api/aprovizionare/proiecte/${n.proiectId}/note-comanda/${n.id}/pdf`)} />
                  </Tooltip>
                  {n.areOriginal && (
                    <Tooltip title="Documentul original (scanat)">
                      <Button size="small" type="text" icon={<PaperClipOutlined />} onClick={() => void original(n)} />
                    </Tooltip>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Modal open={!!deRespins} title={deRespins && `Respinge ${deRespins.numar}${deRespins.necesarNumar ? ` (și necesarul ${deRespins.necesarNumar})` : ''}`}
        okText="Respinge" okButtonProps={{ danger: true, disabled: !motiv.trim(), loading: respingeNota.isPending }} cancelText="Renunță"
        onOk={() => void respinge()} onCancel={() => { setDeRespins(undefined); setMotiv('') }} destroyOnHidden>
        <Input.TextArea rows={3} maxLength={1000} value={motiv} onChange={(e) => setMotiv(e.target.value)} placeholder="Motivul respingerii (obligatoriu)" />
      </Modal>
    </Space>
  )
}
