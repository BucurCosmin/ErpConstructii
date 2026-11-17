import { BulbOutlined, DeleteOutlined, PlusOutlined, WarningOutlined } from '@ant-design/icons'
import { Alert, App, Button, Empty, Input, InputNumber, Modal, Segmented, Space, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { dinValoareTinta, stariAlocare, surseDocument, useAlocaDocument, useAlocaInMasa, valoareTinta } from '../../api/grafic'
import type { AlocareCeruta, CosturiGrafic, StareAlocare, StareDocumentCost } from '../../api/grafic'
import { formatCantitate, formatData, formatLei } from '../../util/format'
import { SelectorTinta, useArboreTinte } from './SelectorTinta'
import type { ContinutGrafic } from '../../api/grafic'

type FiltruStare = 'de-alocat' | 'alocate' | 'toate'

interface RandAlocare {
  cheie: number
  linieIndex: number
  tinta?: string
  procent: number
}

let urmatorul = 1

/** Alocarea pe linii a unui document: fiecare linie de cost direct se împarte procentual pe activități / noduri WBS. */
function AlocareDocument({ proiectId, graficId, stare, arbore, moneda, laInchidere }: {
  proiectId: string
  graficId: string
  stare: StareDocumentCost
  arbore: ReturnType<typeof useArboreTinte>
  moneda: string
  laInchidere: () => void
}) {
  const { message } = App.useApp()
  const aloca = useAlocaDocument(proiectId, graficId)
  const d = stare.document
  const [randuri, setRanduri] = useState<RandAlocare[]>(() =>
    stare.alocari.map((a) => ({ cheie: urmatorul++, linieIndex: a.linieIndex, tinta: valoareTinta(a.tip, a.cheie), procent: a.procent })))
  const [totul, setTotul] = useState<string>()

  const peLinie = (i: number) => randuri.filter((r) => r.linieIndex === i)
  const suma = (i: number) => peLinie(i).reduce((s, r) => s + (r.procent || 0), 0)
  const schimba = (cheie: number, p: Partial<RandAlocare>) => setRanduri((x) => x.map((r) => (r.cheie === cheie ? { ...r, ...p } : r)))
  const adauga = (linieIndex: number, tinta?: string, procent?: number) =>
    setRanduri((x) => [...x, { cheie: urmatorul++, linieIndex, tinta, procent: procent ?? Math.max(0, 100 - x.filter((r) => r.linieIndex === linieIndex).reduce((s, r) => s + r.procent, 0)) }])

  const alocabile = d.linii.filter((l) => l.alocabila)
  const totPe = (t: string | undefined) => {
    if (!t) return
    setRanduri(alocabile.map((l) => ({ cheie: urmatorul++, linieIndex: l.index, tinta: t, procent: 100 })))
  }
  const aplicaPropunerile = () =>
    setRanduri((x) => [...x, ...stare.propuneri.map((p) => ({ cheie: urmatorul++, linieIndex: p.linieIndex, tinta: valoareTinta(p.tip, p.cheie), procent: p.procent }))])

  const salveaza = async () => {
    if (randuri.some((r) => !r.tinta || !r.procent)) {
      message.warning('Fiecare rând de alocare are nevoie de țintă și procent (sau șterge-l).')
      return
    }
    const alocari: AlocareCeruta[] = randuri.map((r) => ({ linieIndex: r.linieIndex, ...dinValoareTinta(r.tinta!), procent: r.procent }))
    try {
      const r = await aloca.mutateAsync({ sursa: d.sursa, documentId: d.id, alocari })
      message.success(`${d.numar}: ${stariAlocare[r.stare].eticheta} (${r.procentAlocat.toLocaleString('ro-RO')}%).`)
      laInchidere()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const leiLinie = (v: number) => (d.moneda === 'RON' ? v : stare.cursDocument ? v * stare.cursDocument : null)

  return (
    <Modal open onCancel={laInchidere} width="min(1250px, 97vw)" okText="Salvează alocarea" cancelText="Renunță" onOk={() => void salveaza()} confirmLoading={aloca.isPending}
      title={<Space wrap>{surseDocument[d.sursa]} {d.numar}<Typography.Text type="secondary" style={{ fontWeight: 400 }}>{formatData(d.data)} · {d.partener}{d.detalii ? ` · ${d.detalii}` : ''}</Typography.Text></Space>}>
      <Space orientation="vertical" size={10} style={{ width: '100%' }}>
        {d.moneda !== 'RON' && (
          <Typography.Text type="secondary">
            Document în {d.moneda}: curs BNR {stare.cursDocument?.toLocaleString('ro-RO', { maximumFractionDigits: 4 }) ?? '— (lipsește)'} lei din {formatData(stare.dataCurs)}.
          </Typography.Text>
        )}
        {stare.probleme.map((p, i) => <Alert key={i} type="warning" showIcon title={p} />)}
        <Space wrap>
          <Typography.Text>Tot documentul pe:</Typography.Text>
          <SelectorTinta arbore={arbore} value={totul} onChange={setTotul} style={{ width: 520 }} />
          <Button disabled={!totul} onClick={() => totPe(totul)}>Aplică pe toate liniile (100%)</Button>
          {stare.propuneri.length > 0 && (
            <Tooltip title={stare.propuneri.map((p) => `linia ${p.linieIndex + 1}: ${p.motiv}`).join('; ')}>
              <Button icon={<BulbOutlined />} onClick={aplicaPropunerile}>Aplică propunerile ({stare.propuneri.length})</Button>
            </Tooltip>
          )}
        </Space>

        <Table className="tabel-compact" size="small" pagination={false} rowKey="index" dataSource={d.linii} scroll={{ y: 520 }}
          rowClassName={(l) => (l.alocabila ? '' : 'rand-inactiv')}
          columns={[
            { title: '#', dataIndex: 'index', width: 40, render: (i: number) => i + 1 },
            {
              title: 'Linia', dataIndex: 'denumire', width: 300,
              render: (t: string, l) => <>{t}{l.nota && <div><Typography.Text type="secondary" style={{ fontSize: 11 }}>{l.nota}</Typography.Text></div>}</>,
            },
            { title: 'Cant.', width: 90, className: 'numeric', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura ?? ''}` },
            {
              title: `Valoare (${d.moneda})`, dataIndex: 'valoare', width: 110, className: 'numeric', render: (v: number) => formatLei(v),
            },
            {
              title: 'Alocare (țintă · procent)', key: 'alocare',
              render: (_, l) => {
                if (!l.alocabila) return <Typography.Text type="secondary">nu se alocă</Typography.Text>
                const s = suma(l.index)
                return (
                  <Space orientation="vertical" size={4} style={{ width: '100%' }}>
                    {peLinie(l.index).map((r) => (
                      <Space key={r.cheie} style={{ width: '100%' }} styles={{ item: { flex: 'none' } }}>
                        <SelectorTinta arbore={arbore} value={r.tinta} onChange={(v) => schimba(r.cheie, { tinta: v })} style={{ width: 440 }} />
                        <InputNumber size="small" min={0.01} max={100} value={r.procent} onChange={(v) => schimba(r.cheie, { procent: Number(v ?? 0) })} suffix="%" style={{ width: 95 }} />
                        <Typography.Text type="secondary" style={{ fontSize: 12, minWidth: 90, display: 'inline-block', textAlign: 'right' }}>
                          {(() => { const lei = leiLinie(l.valoare); return lei === null ? '' : `${formatLei((lei * r.procent) / 100)} lei` })()}
                        </Typography.Text>
                        <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setRanduri((x) => x.filter((y) => y.cheie !== r.cheie))} />
                      </Space>
                    ))}
                    <Space>
                      <Button size="small" type="dashed" icon={<PlusOutlined />} onClick={() => adauga(l.index)} disabled={s >= 100}>țintă</Button>
                      <Typography.Text type={s > 100.01 ? 'danger' : s >= 99.99 ? 'success' : 'warning'} style={{ fontSize: 12 }}>
                        {s.toLocaleString('ro-RO', { maximumFractionDigits: 2 })}% alocat
                      </Typography.Text>
                    </Space>
                  </Space>
                )
              },
            },
          ]} />
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Valorile se convertesc în {moneda} la cursul BNR din data documentului. Liniile de materiale de stoc de pe facturi nu se alocă: costul lor vine prin bonurile de consum.
        </Typography.Text>
      </Space>
    </Modal>
  )
}

/**
 * Alocarea costurilor pe grafic (Grafic → Alocare costuri, D-57): facturile și bonurile de consum ale proiectului, cu starea
 * alocării (procent din liniile de cost direct). Un document se alocă pe linii, procentual; mai multe documente odată se pun
 * întregi pe aceeași țintă.
 */
export function AlocareCosturi({ proiectId, graficId, costuri, continutCurent, poateEdita }: {
  proiectId: string
  graficId: string
  costuri: CosturiGrafic
  continutCurent: ContinutGrafic | undefined
  poateEdita: boolean
}) {
  const { message } = App.useApp()
  const arbore = useArboreTinte(continutCurent)
  const inMasa = useAlocaInMasa(proiectId, graficId)
  const [filtru, setFiltru] = useState<FiltruStare>('de-alocat')
  const [cauta, setCauta] = useState('')
  const [selectate, setSelectate] = useState<string[]>([])
  const [tinta, setTinta] = useState<string>()
  const [deschis, setDeschis] = useState<StareDocumentCost>()
  const m = costuri.moneda
  const cheieDoc = (s: StareDocumentCost) => `${s.document.sursa}:${s.document.id}`

  const documente = useMemo(() => {
    const t = cauta.trim().toLowerCase()
    return costuri.documente.filter((s) =>
      (filtru === 'toate' || (filtru === 'alocate' ? s.stare === 'Alocat' : s.stare === 'Nealocat' || s.stare === 'Partial'))
      && (!t || s.document.numar.toLowerCase().includes(t) || (s.document.partener ?? '').toLowerCase().includes(t)
        || s.document.linii.some((l) => l.denumire.toLowerCase().includes(t))))
  }, [costuri, filtru, cauta])
  const deAlocat = costuri.documente.filter((s) => s.stare === 'Nealocat' || s.stare === 'Partial')
  const nealocatLei = deAlocat.reduce((x, s) => x + s.valoareLei - s.alocatLei, 0)

  const alocaSelectia = async () => {
    if (!tinta) return
    try {
      const n = await inMasa.mutateAsync({
        documente: costuri.documente.filter((s) => selectate.includes(cheieDoc(s))).map((s) => ({ sursa: s.document.sursa, documentId: s.document.id })),
        ...dinValoareTinta(tinta),
      })
      message.success(`${n} documente alocate.`)
      setSelectate([])
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const denumireTinta = (tip: string, cheie: string) => costuri.alocari.find((a) => a.tip === tip && a.cheie === cheie)?.denumireTinta ?? cheie

  const coloane: TableColumnsType<StareDocumentCost> = [
    { title: 'Tip', key: 'tip', width: 110, render: (_, s) => surseDocument[s.document.sursa] },
    {
      title: 'Document', key: 'numar', width: 170,
      render: (_, s) => (s.document.sursa === 'Factura' ? <Link to={`/facturi?factura=${s.document.id}`}>{s.document.numar}</Link> : s.document.numar),
    },
    { title: 'Data', key: 'data', width: 95, render: (_, s) => formatData(s.document.data), sorter: (a, b) => a.document.data.localeCompare(b.document.data) },
    { title: 'Partener / gestiune', key: 'partener', width: 220, ellipsis: true, render: (_, s) => s.document.partener },
    {
      title: 'Linii', key: 'linii', ellipsis: true,
      render: (_, s) => <Tooltip title={s.document.linii.map((l) => `${l.index + 1}. ${l.denumire}${l.alocabila ? '' : ' (stoc)'}`).join('\n')}>{s.document.linii.map((l) => l.denumire).join('; ')}</Tooltip>,
    },
    {
      title: 'Cost direct (lei)', key: 'valoare', width: 125, className: 'numeric', sorter: (a, b) => a.valoareLei - b.valoareLei,
      render: (_, s) => <>{formatLei(s.valoareLei)}{s.document.moneda !== 'RON' && <div><Typography.Text type="secondary" style={{ fontSize: 11 }}>în {s.document.moneda}</Typography.Text></div>}</>,
    },
    {
      title: 'Alocat', key: 'stare', width: 130,
      filters: (['Nealocat', 'Partial', 'Alocat', 'FaraCost'] as StareAlocare[]).map((x) => ({ text: stariAlocare[x].eticheta, value: x })),
      onFilter: (v, s) => s.stare === v,
      render: (_, s) => (
        <Space size={2}>
          <Tag color={stariAlocare[s.stare].culoare}>{s.stare === 'Partial' ? `${s.procentAlocat.toLocaleString('ro-RO')}%` : stariAlocare[s.stare].eticheta}</Tag>
          {s.probleme.length > 0 && <Tooltip title={s.probleme.join(' ')}><WarningOutlined style={{ color: '#fa8c16' }} /></Tooltip>}
          {s.propuneri.length > 0 && <Tooltip title={`Propuneri: ${s.propuneri.map((p) => p.motiv).join('; ')}`}><BulbOutlined style={{ color: '#1677ff' }} /></Tooltip>}
        </Space>
      ),
    },
    {
      title: 'Ținte', key: 'tinte', width: 260, ellipsis: true,
      render: (_, s) => [...new Set(s.alocari.map((a) => `${a.tip}\u0001${a.cheie}`))].map((k) => {
        const { tip, cheie } = dinValoareTinta(k)
        return <Tooltip key={k} title={denumireTinta(tip, cheie)}><Tag style={{ marginBottom: 2 }}>{tip === 'Wbs' ? '▸ ' : ''}{cheie}</Tag></Tooltip>
      }),
    },
    {
      key: 'actiune', width: 80,
      render: (_, s) => (s.stare !== 'FaraCost' && poateEdita ? <Button size="small" onClick={() => setDeschis(s)}>Alocă</Button> : null),
    },
  ]

  if (costuri.surse.length === 0) return <Empty description="Modulele Facturi și Stocuri nu sunt disponibile: nu există documente de alocat." />

  return (
    <Space orientation="vertical" size={10} style={{ width: '100%' }}>
      <Alert type={deAlocat.length ? 'info' : 'success'} showIcon
        title={deAlocat.length
          ? `${deAlocat.length} documente au costuri nealocate pe grafic: ${formatLei(nealocatLei)} lei.`
          : 'Toate costurile directe ale proiectului sunt alocate pe grafic.'}
        description={`Alocat: ${formatLei(costuri.total.lei)} lei${costuri.total.monedaGrafic !== null ? ` = ${formatLei(costuri.total.monedaGrafic)} ${m}` : ''} (curs BNR din data fiecărui document).`
          + (costuri.faraTinta.lei ? ` Dintre care ${formatLei(costuri.faraTinta.lei)} lei pe ținte care nu mai sunt în actualizarea curentă.` : '')} />
      <Space wrap>
        <Segmented<FiltruStare> value={filtru} onChange={setFiltru}
          options={[{ value: 'de-alocat', label: `De alocat (${deAlocat.length})` }, { value: 'alocate', label: 'Alocate' }, { value: 'toate', label: 'Toate' }]} />
        <Input.Search allowClear placeholder="Număr, partener sau linie" style={{ width: 280 }} onChange={(e) => setCauta(e.target.value)} />
        {poateEdita && selectate.length > 0 && (
          <Space.Compact>
            <SelectorTinta arbore={arbore} value={tinta} onChange={setTinta} style={{ width: 420 }} placeholder={`Alocă cele ${selectate.length} documente pe…`} />
            <Button type="primary" disabled={!tinta} loading={inMasa.isPending} onClick={() => void alocaSelectia()}>Alocă restul pe țintă</Button>
          </Space.Compact>
        )}
      </Space>
      <Table<StareDocumentCost> className="tabel-compact" size="small" rowKey={cheieDoc} dataSource={documente} columns={coloane}
        pagination={documente.length > 100 ? { pageSize: 100, size: 'small' } : false} scroll={{ x: 1400 }}
        rowSelection={poateEdita ? { selectedRowKeys: selectate, onChange: (k) => setSelectate(k as string[]), getCheckboxProps: (s) => ({ disabled: s.stare === 'FaraCost' }) } : undefined}
        locale={{ emptyText: filtru === 'de-alocat' ? 'Nimic de alocat.' : 'Niciun document.' }} />
      {deschis && (
        <AlocareDocument proiectId={proiectId} graficId={graficId} stare={deschis} arbore={arbore} moneda={m} laInchidere={() => setDeschis(undefined)} />
      )}
    </Space>
  )
}
