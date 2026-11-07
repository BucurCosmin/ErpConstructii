import { DownloadOutlined, PrinterOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, Select, Space, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { useGestiuni, useTrasabilitate } from '../../api/stocuri'
import type { FiltreTrasabilitate, LotStoc } from '../../api/stocuri'
import { LinkFactura } from '../../componente/LinkFactura'
import { formatCantitate, formatLei } from '../../util/format'

const bani = (v: number | null) => (v === null ? '—' : formatLei(v))
const data = (d: string | null) => (d ? dayjs(d).format('DD.MM.YYYY') : '')

interface GrupMaterial {
  cheie: string
  gestiune: string
  gestiuneTip: LotStoc['gestiuneTip']
  articolCod: string
  denumire: string
  unitateMasura: string
  cantitate: number
  valoare: number | null
  loturi: LotStoc[]
}

/** Raportul pentru Excel: o linie pe lot, separator „;” și BOM (Excel în română îl deschide direct). */
export function exportaCsv(loturi: LotStoc[]) {
  const cap = ['Gestiune', 'Cod material', 'Material', 'UM', 'Cantitate', 'Intrat la', 'Document intrare', 'Factura', 'Data facturii', 'Furnizor',
    'Proiect factură', 'Preț intrare', 'Valoare']
  const camp = (v: string | number | null) => {
    if (v === null) return ''
    const s = typeof v === 'number' ? v.toString().replace('.', ',') : v
    return /[;"\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  const randuri = loturi.map((l) => [l.gestiune, l.articolCod, l.denumire, l.unitateMasura, l.cantitate, data(l.dataIntrare), l.documentNumar,
    l.facturaNumar, data(l.facturaData), l.furnizorDenumire, l.facturaProiectCod, l.pretIntrare, l.valoare].map(camp).join(';'))
  const blob = new Blob(['﻿' + [cap.join(';'), ...randuri].join('\r\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `provenienta-stoc-${dayjs().format('YYYY-MM-DD')}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

/**
 * Proveniența stocului: pentru fiecare material din depozite / șantiere, pe ce NIR, ce factură și de la ce furnizor a intrat
 * cantitatea aflată acum în gestiune (ieșirile se atribuie FIFO; materialul transferat sau returnat își păstrează originea).
 */
export function ProvenientaStoc() {
  const gestiuni = useGestiuni()
  const [filtre, setFiltre] = useState<FiltreTrasabilitate>({ tip: 'Depozit' })
  const loturi = useTrasabilitate(filtre)

  const grupuri = useMemo(() => {
    const m = new Map<string, GrupMaterial>()
    for (const l of loturi.data ?? []) {
      const cheie = `${l.gestiuneId}|${l.articolId}`
      const g = m.get(cheie) ?? { cheie, gestiune: l.gestiune, gestiuneTip: l.gestiuneTip, articolCod: l.articolCod, denumire: l.denumire,
        unitateMasura: l.unitateMasura, cantitate: 0, valoare: l.valoare === null ? null : 0, loturi: [] }
      g.cantitate += l.cantitate
      if (g.valoare !== null && l.valoare !== null) g.valoare += l.valoare
      g.loturi.push(l)
      m.set(cheie, g)
    }
    return [...m.values()]
  }, [loturi.data])
  const cuPreturi = (loturi.data ?? []).some((l) => l.valoare !== null)

  return (
    <Card
      title="Proveniența stocului"
      extra={
        <Space>
          <Button icon={<DownloadOutlined />} disabled={!loturi.data?.length} onClick={() => exportaCsv(loturi.data ?? [])}>Excel (CSV)</Button>
          <Button icon={<PrinterOutlined />} disabled={!loturi.data?.length} onClick={() => window.print()}>Tipărește</Button>
        </Space>
      }
    >
      <Typography.Paragraph type="secondary">
        Pentru fiecare material din gestiune: pe ce NIR și ce factură a intrat. Se consideră că iese întâi ce a intrat primul (FIFO), deci în
        stoc rămân cele mai recente intrări; materialul trimis în șantier sau returnat în depozit își păstrează factura de origine.
        Valoarea e la prețul de intrare (NIR), nu la costul mediu din evidență.
      </Typography.Paragraph>
      <Form layout="inline" style={{ marginBottom: 16, rowGap: 8 }} initialValues={filtre}
        onFinish={(v: FiltreTrasabilitate) => setFiltre({
          tip: v.gestiuneId ? undefined : v.tip, gestiuneId: v.gestiuneId,
          material: v.material?.trim() || undefined, cauta: v.cauta?.trim() || undefined,
        })}>
        <Form.Item name="tip">
          <Select allowClear placeholder="Orice gestiune" style={{ width: 170 }}
            options={[{ value: 'Depozit', label: 'Depozite' }, { value: 'Santier', label: 'Șantiere' }]} />
        </Form.Item>
        <Form.Item name="gestiuneId">
          <Select allowClear placeholder="Toate gestiunile" style={{ width: 260 }} showSearch={{ optionFilterProp: 'label' }}
            options={gestiuni.data?.map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} />
        </Form.Item>
        <Form.Item name="material">
          <Input allowClear prefix={<SearchOutlined />} placeholder="Material (denumire sau cod)" style={{ width: 240 }} />
        </Form.Item>
        <Form.Item name="cauta">
          <Input allowClear placeholder="Factura, NIR sau furnizor" style={{ width: 220 }} />
        </Form.Item>
        <Button type="primary" htmlType="submit">Afișează</Button>
      </Form>

      {loturi.error && <Alert type="error" showIcon title={(loturi.error as Error).message} style={{ marginBottom: 16 }} />}

      <Table<GrupMaterial>
        rowKey="cheie"
        size="small"
        className="tabel-compact"
        tableLayout="fixed"
        loading={loturi.isLoading}
        dataSource={grupuri}
        pagination={{ pageSize: 50, hideOnSinglePage: true }}
        locale={{ emptyText: 'Niciun material în stoc pentru filtrul ales.' }}
        expandable={{
          defaultExpandAllRows: true,
          expandedRowRender: (g) => (
            <Table<LotStoc>
              size="small"
              className="tabel-compact"
              pagination={false}
              rowKey={(l) => `${l.documentId}-${l.dataIntrare}-${l.cantitate}`}
              dataSource={g.loturi}
              columns={[
                { title: 'Cantitate', width: 130, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
                { title: 'Intrat la', dataIndex: 'dataIntrare', width: 100, render: data },
                {
                  title: 'Document', width: 170,
                  render: (_, l) => l.origine === 'NIR' ? l.documentNumar
                    : <>{l.documentNumar} <Tag color={l.origine === 'Inventar' ? 'purple' : 'default'}>{l.origine === 'Inventar' ? 'plus inventar' : 'transfer'}</Tag></>,
                },
                { title: 'Factura', width: 170, render: (_, l) => l.facturaNumar && <><LinkFactura id={l.facturaId} numar={l.facturaNumar} /><div><Typography.Text type="secondary">{data(l.facturaData)}</Typography.Text></div></> },
                { title: 'Furnizor', dataIndex: 'furnizorDenumire' },
                { title: 'Proiect factură', dataIndex: 'facturaProiectCod', width: 130 },
                ...(cuPreturi ? [
                  { title: 'Preț intrare', dataIndex: 'pretIntrare', width: 110, align: 'right' as const, render: bani },
                  { title: 'Valoare', dataIndex: 'valoare', width: 120, align: 'right' as const, render: bani },
                ] : []),
              ]}
            />
          ),
        }}
        columns={[
          { title: 'Gestiune', dataIndex: 'gestiune', width: 240 },
          { title: 'Cod', dataIndex: 'articolCod', width: 110 },
          { title: 'Material', dataIndex: 'denumire', ellipsis: { showTitle: true } },
          { title: 'În stoc', width: 140, align: 'right', render: (_, g) => <Typography.Text strong>{formatCantitate(g.cantitate)} {g.unitateMasura}</Typography.Text> },
          { title: 'Intrări', width: 90, align: 'right', render: (_, g) => new Set(g.loturi.map((l) => l.documentId)).size },
          ...(cuPreturi ? [{ title: 'Valoare (preț intrare)', width: 160, align: 'right' as const, render: (_: unknown, g: GrupMaterial) => bani(g.valoare) }] : []),
        ]}
      />
    </Card>
  )
}
