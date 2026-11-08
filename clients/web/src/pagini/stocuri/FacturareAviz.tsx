import { Alert, App, Checkbox, Empty, Flex, Modal, Select, Space, Spin, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { useFacturareAviz, useFactureazaAvize } from '../../api/stocuri'
import type { FacturareAviz as DateFacturare } from '../../api/stocuri'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatLei } from '../../util/format'

type LinieNir = DateFacturare['nir'][number]['linii'][number]

const aceeasiUm = (a: string, b: string) => a.trim().replace(/\.$/, '').toLowerCase() === b.trim().replace(/\.$/, '').toLowerCase()

/**
 * Factura venită după marfa recepționată pe aviz (D-40): fiecare material din NIR-urile pe avizele notei de comandă a
 * facturii primește linia facturii care îl acoperă (propusă după material / denumire). La confirmare NIR-urile primesc
 * factura și prețurile ei, iar stocul diferența de valoare; liniile acoperite nu mai sunt de recepționat.
 */
export function FacturareAviz({ facturaId, onInchide }: { facturaId?: string; onInchide: () => void }) {
  const { message, modal } = App.useApp()
  const date = useFacturareAviz(facturaId)
  const factureaza = useFactureazaAvize()
  const d = date.data
  /** Pe NIR și linie: linia facturii aleasă și prețul pe UM-ul NIR-ului (doar când UM-urile diferă). */
  const [alegeri, setAlegeri] = useState<Record<string, { indexFactura: number | null; pret: number | null }>>({})
  const [nirAlese, setNirAlese] = useState<string[]>([])

  useEffect(() => {
    if (!d) return
    const a: Record<string, { indexFactura: number | null; pret: number | null }> = {}
    for (const n of d.nir) for (const l of n.linii) a[`${n.nirId}:${l.indexAviz}`] = { indexFactura: l.indexFacturaPropus, pret: null }
    setAlegeri(a)
    setNirAlese(d.nir.map((n) => n.nirId))
  }, [d])

  const liniaFacturii = (i: number | null) => (i === null ? undefined : d?.liniiFactura.find((l) => l.index === i))
  const lipsa = d?.nir.filter((n) => nirAlese.includes(n.nirId)).flatMap((n) => n.linii.filter((l) => alegeri[`${n.nirId}:${l.indexAviz}`]?.indexFactura == null)) ?? []

  const trimite = async () => {
    if (!d) return
    if (nirAlese.length === 0) {
      message.warning('Bifează cel puțin un NIR pe aviz.')
      return
    }
    if (lipsa.length > 0) {
      message.warning(`Alege linia facturii pentru: ${lipsa.map((l) => l.denumire).join(', ')}.`)
      return
    }
    try {
      const r = await factureaza.mutateAsync({
        facturaId: d.facturaId,
        data: null,
        nir: d.nir.filter((n) => nirAlese.includes(n.nirId)).map((n) => ({
          nirId: n.nirId,
          linii: n.linii.map((l) => {
            const a = alegeri[`${n.nirId}:${l.indexAviz}`]
            return { indexAviz: l.indexAviz, indexFactura: a.indexFactura!, pretUnitar: a.pret }
          }),
        })),
      })
      onInchide()
      if (r.avertismente.length > 0) {
        modal.warning({ title: r.mesaj, width: 640, content: <ul style={{ paddingInlineStart: 18 }}>{r.avertismente.map((a, i) => <li key={i}>{a}</li>)}</ul> })
      } else {
        message.success(r.mesaj)
      }
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal
      open={!!facturaId}
      onCancel={onInchide}
      width="min(1300px, 96vw)"
      style={{ top: 24 }}
      title={d ? `Leagă factura ${d.numar} din ${dayjs(d.data).format('DD.MM.YYYY')} (${d.furnizorDenumire}) de marfa recepționată pe aviz` : 'Factura pentru marfa recepționată pe aviz'}
      okText="Leagă și preia prețurile facturii"
      cancelText="Renunță"
      onOk={() => void trimite()}
      okButtonProps={{ disabled: !d || d.nir.length === 0 }}
      confirmLoading={factureaza.isPending}
      destroyOnHidden
    >
      {!d ? (
        <Flex justify="center" style={{ padding: 48 }}><Spin /></Flex>
      ) : d.nir.length === 0 ? (
        <Empty description={`Nu există recepții pe aviz fără factură pentru nota de comandă ${d.notaComanda ?? 'a facturii'}.`} />
      ) : (
        <Space orientation="vertical" style={{ width: '100%' }} size="middle">
          <Alert
            type="info"
            showIcon
            title={`NIR-urile pe avizele notei de comandă ${d.notaComanda ?? '—'} primesc factura și prețurile ei.`}
            description="Pentru fiecare material alege linia facturii care îl acoperă (propusă automat după material sau denumire). Diferența de valoare intră în stoc unde e acum materialul (depozit sau șantier); liniile facturii acoperite nu mai sunt de recepționat."
          />
          <Table
            size="small"
            pagination={false}
            rowKey="index"
            dataSource={d.liniiFactura}
            title={() => <Typography.Text strong>Liniile de stoc ale facturii</Typography.Text>}
            columns={[
              { title: '#', width: 40, render: (_, l) => l.index + 1 },
              { title: 'Pe factură', dataIndex: 'denumire' },
              { title: 'Cantitate', width: 140, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
              ...(d.vedePreturi ? [{ title: 'Preț', width: 120, align: 'right' as const, render: (_: unknown, l: DateFacturare['liniiFactura'][number]) => (l.pretUnitar === null ? '—' : formatLei(l.pretUnitar)) }] : []),
              { title: 'Neacoperit încă', width: 140, align: 'right', render: (_, l) => `${formatCantitate(l.ramas)} ${l.unitateMasura}` },
            ]}
          />
          {d.nir.map((n) => (
            <Table<LinieNir>
              key={n.nirId}
              size="small"
              pagination={false}
              rowKey="indexAviz"
              dataSource={n.linii}
              title={() => (
                <Checkbox checked={nirAlese.includes(n.nirId)} onChange={(e) => setNirAlese((x) => (e.target.checked ? [...x, n.nirId] : x.filter((y) => y !== n.nirId)))}>
                  <Typography.Text strong>{n.numar}</Typography.Text> din {dayjs(n.data).format('DD.MM.YYYY')} · avizul {n.avizNumar} · {n.gestiune}
                </Checkbox>
              )}
              columns={[
                {
                  title: 'Material recepționat',
                  render: (_, l) => (
                    <Space orientation="vertical" size={0}>
                      <Typography.Text>{l.articolCod} — {l.denumire}</Typography.Text>
                      {l.denumireAviz !== l.denumire && <Typography.Text type="secondary">pe aviz: {l.denumireAviz}</Typography.Text>}
                    </Space>
                  ),
                },
                { title: 'Cantitate', width: 140, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
                ...(d.vedePreturi ? [{ title: 'Preț acum', width: 110, align: 'right' as const, render: (_: unknown, l: LinieNir) => (l.pretUnitar === null ? '—' : formatLei(l.pretUnitar)) }] : []),
                {
                  title: 'Linia facturii',
                  width: 380,
                  render: (_, l) => {
                    const cheie = `${n.nirId}:${l.indexAviz}`
                    const a = alegeri[cheie]
                    const lf = liniaFacturii(a?.indexFactura ?? null)
                    const umDiferit = lf && !aceeasiUm(lf.unitateMasura, l.unitateMasura)
                    return (
                      <Space orientation="vertical" size={4} style={{ width: '100%' }}>
                        <Select
                          style={{ width: '100%' }}
                          placeholder="Alege linia facturii"
                          status={a?.indexFactura == null && nirAlese.includes(n.nirId) ? 'warning' : undefined}
                          value={a?.indexFactura ?? undefined}
                          onChange={(v: number) => setAlegeri((x) => ({ ...x, [cheie]: { indexFactura: v, pret: null } }))}
                          options={d.liniiFactura.map((x) => ({
                            value: x.index,
                            label: `${x.index + 1}. ${x.denumire} (${formatCantitate(x.cantitate)} ${x.unitateMasura}${d.vedePreturi && x.pretUnitar !== null ? ` × ${formatLei(x.pretUnitar)}` : ''})`,
                          }))}
                        />
                        {umDiferit && (
                          <Space size={4} wrap>
                            <Tooltip title="Factura are altă unitate de măsură decât recepția: scrie prețul pe UM-ul NIR-ului (altfel se preia prețul facturii ca atare).">
                              <Tag color="orange">UM diferit: {lf.unitateMasura} pe factură</Tag>
                            </Tooltip>
                            {d.vedePreturi && (
                              <NumarRo
                                size="small"
                                min={0}
                                style={{ width: 170 }}
                                placeholder={`preț / ${l.unitateMasura}`}
                                value={a?.pret ?? null}
                                onChange={(v) => setAlegeri((x) => ({ ...x, [cheie]: { ...x[cheie], pret: v ?? null } }))}
                              />
                            )}
                          </Space>
                        )}
                      </Space>
                    )
                  },
                },
              ]}
            />
          ))}
        </Space>
      )}
    </Modal>
  )
}
