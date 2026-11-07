import { CheckOutlined, DeleteOutlined, PlusOutlined, SaveOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, DatePicker, Drawer, Form, Input, Modal, Popconfirm, Select, Space, Statistic, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import {
  useArticoleStoc,
  useDeschideInventar,
  useFinalizeazaInventar,
  useGestiuni,
  useInventare,
  useNumaraInventar,
  useRenuntaInventar,
} from '../../api/stocuri'
import type { Inventar, LinieInventar } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'
import { NumarRo } from '../../componente/NumarRo'

const bani = (v: number | null) => (v === null ? '—' : formatLei(v))

type Numarare = Record<string, { faptic: number | null; pret: number | null }>

/** Fișa unui inventar: comisia introduce cantitățile numărate; finalizarea (cu drept de aprobare) înregistrează diferențele. */
function FisaInventar({ inventar, onInchide, poateEdita, poateAproba }: { inventar: Inventar; onInchide: () => void; poateEdita: boolean; poateAproba: boolean }) {
  const { message } = App.useApp()
  const articole = useArticoleStoc()
  const numara = useNumaraInventar()
  const finalizeaza = useFinalizeazaInventar()
  const renunta = useRenuntaInventar()
  // Starea se inițializează din inventar; după salvare, fișa se remontează (cheia din părinte) cu datele noi.
  const [valori, setValori] = useState<Numarare>(() =>
    Object.fromEntries(inventar.linii.map((l) => [l.articolId, { faptic: l.cantitateFaptica, pret: l.pretUnitarPlus }])))
  const [linii, setLinii] = useState<LinieInventar[]>(inventar.linii)
  const deschis = inventar.stare === 'Deschis'
  const editabil = deschis && poateEdita

  const adaugaGasit = (articolId: string) => {
    const a = articole.data?.find((x) => x.id === articolId)
    if (!a || linii.some((l) => l.articolId === articolId)) return
    setLinii([...linii, { articolId: a.id, articolCod: a.cod, denumire: a.denumire, unitateMasura: a.unitateMasura, cantitateScriptica: 0, valoareScriptica: 0,
      cantitateFaptica: null, diferenta: null, pretUnitarPlus: null, valoareDiferenta: null }])
  }

  const salveaza = async () => {
    try {
      await numara.mutateAsync({
        id: inventar.id,
        linii: linii.map((l) => ({ articolId: l.articolId, cantitateFaptica: valori[l.articolId]?.faptic ?? null, pretUnitarPlus: valori[l.articolId]?.pret ?? null })),
      })
      message.success('Numărătoarea a fost salvată.')
      return true
    } catch (e) {
      message.error((e as Error).message)
      return false
    }
  }

  const finalizare = async () => {
    if (!(await salveaza())) return
    try {
      const inv = await finalizeaza.mutateAsync(inventar.id)
      message.success(`${inv.numar} finalizat: diferențele au fost înregistrate în stoc.`)
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const nenumarate = linii.filter((l) => valori[l.articolId]?.faptic == null).length

  return (
    <Drawer open onClose={onInchide} size={1100} title={`${inventar.numar} — ${inventar.gestiune}`}
      extra={
        deschis ? (
          <Space>
            {poateEdita && (
              <Popconfirm title="Renunți la acest inventar? Gestiunea se deblochează." okText="Renunță" cancelText="Nu"
                onConfirm={async () => { await renunta.mutateAsync(inventar.id); onInchide() }}>
                <Button danger icon={<DeleteOutlined />}>Renunță</Button>
              </Popconfirm>
            )}
            {editabil && <Button icon={<SaveOutlined />} loading={numara.isPending} onClick={() => void salveaza()}>Salvează numărătoarea</Button>}
            {poateAproba && (
              <Popconfirm title="Finalizezi inventarul? Plusurile și minusurile se înregistrează în stoc, iar documentul devine definitiv." okText="Finalizează"
                cancelText="Nu" onConfirm={() => void finalizare()} disabled={nenumarate > 0}>
                <Button type="primary" icon={<CheckOutlined />} disabled={nenumarate > 0} loading={finalizeaza.isPending}>Finalizează</Button>
              </Popconfirm>
            )}
          </Space>
        ) : <Tag color="green">finalizat de {inventar.finalizatDe}</Tag>
      }
    >
      {deschis && (
        <Alert type="info" showIcon style={{ marginBottom: 12 }}
          title={`Gestiunea e blocată pentru alte mișcări până la finalizare. Numărate: ${linii.length - nenumarate} din ${linii.length}.`}
          description="Introdu cantitatea găsită la fiecare material (0 dacă lipsește). Pentru plusurile unui material fără preț mediu în evidență, completează prețul unitar." />
      )}
      {!deschis && (
        <Space size="large" style={{ marginBottom: 12 }}>
          <Statistic title="Plusuri" value={inventar.valoarePlusuri ?? 0} formatter={(v) => formatLei(Number(v))} />
          <Statistic title="Minusuri" value={inventar.valoareMinusuri ?? 0} formatter={(v) => formatLei(Number(v))} />
        </Space>
      )}
      {inventar.comisie && <Typography.Paragraph type="secondary">Comisia: {inventar.comisie}</Typography.Paragraph>}
      <Table<LinieInventar>
        rowKey="articolId"
        size="small"
        pagination={false}
        dataSource={linii}
        columns={[
          { title: 'Cod', dataIndex: 'articolCod', width: 110 },
          { title: 'Material', dataIndex: 'denumire' },
          { title: 'Scriptic', width: 120, align: 'right', render: (_, l) => `${formatCantitate(l.cantitateScriptica)} ${l.unitateMasura}` },
          {
            title: 'Faptic', width: 140,
            render: (_, l) => editabil
              ? <NumarRo min={0} style={{ width: '100%' }} value={valori[l.articolId]?.faptic ?? null}
                  onChange={(v) => setValori({ ...valori, [l.articolId]: { faptic: v, pret: valori[l.articolId]?.pret ?? null } })} />
              : l.cantitateFaptica === null ? '—' : formatCantitate(l.cantitateFaptica),
          },
          {
            title: 'Diferență', width: 110, align: 'right',
            render: (_, l) => {
              const f = editabil ? valori[l.articolId]?.faptic : l.cantitateFaptica
              if (f == null) return '—'
              const d = f - l.cantitateScriptica
              return <Typography.Text type={d < 0 ? 'danger' : d > 0 ? 'success' : undefined}>{d > 0 ? '+' : ''}{formatCantitate(d)}</Typography.Text>
            },
          },
          {
            title: 'Preț plus', width: 120,
            render: (_, l) => {
              const f = valori[l.articolId]?.faptic
              const cerePret = l.cantitateScriptica === 0 && f != null && f > 0
              if (!editabil) return l.pretUnitarPlus === null ? '' : formatLei(l.pretUnitarPlus)
              return cerePret ? <NumarRo min={0} style={{ width: '100%' }} placeholder="lei/UM" value={valori[l.articolId]?.pret ?? null}
                onChange={(v) => setValori({ ...valori, [l.articolId]: { faptic: f ?? null, pret: v } })} /> : ''
            },
          },
          { title: 'Valoare diferență', dataIndex: 'valoareDiferenta', width: 140, align: 'right', render: bani },
        ]}
      />
      {editabil && (
        <Select showSearch={{ optionFilterProp: 'label' }} placeholder="➕ Material găsit care nu e în evidență" style={{ width: 460, marginTop: 12 }} value={null}
          onChange={(v) => v && adaugaGasit(v)}
          options={(articole.data ?? []).filter((a) => a.activ && !linii.some((l) => l.articolId === a.id)).map((a) => ({ value: a.id, label: `${a.cod} — ${a.denumire}` }))} />
      )}
    </Drawer>
  )
}

/** Inventarele gestiunilor: deschidere, numărătoare, finalizare. */
export function InventarGestiune({ poateEdita, poateAproba }: { poateEdita: boolean; poateAproba: boolean }) {
  const { message } = App.useApp()
  const inventare = useInventare()
  const gestiuni = useGestiuni()
  const deschide = useDeschideInventar()
  const [nou, setNou] = useState(false)
  const [deschisId, setDeschisId] = useState<string>()
  const [form] = Form.useForm<{ gestiuneId: string; data: Dayjs; comisie?: string }>()
  const deschisCurent = useMemo(() => inventare.data?.find((i) => i.id === deschisId), [inventare.data, deschisId])

  return (
    <Card title="Inventar" extra={poateEdita && <Button type="primary" icon={<PlusOutlined />} onClick={() => setNou(true)}>Inventar nou</Button>}>
      <Table<Inventar>
        rowKey="id"
        size="small"
        loading={inventare.isLoading}
        dataSource={inventare.data}
        pagination={{ pageSize: 15, hideOnSinglePage: true }}
        onRow={(i) => ({ onClick: () => setDeschisId(i.id), style: { cursor: 'pointer' } })}
        columns={[
          { title: 'Număr', dataIndex: 'numar', width: 140 },
          { title: 'Data', dataIndex: 'data', width: 110, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
          { title: 'Gestiune', dataIndex: 'gestiune' },
          { title: 'Stare', dataIndex: 'stare', width: 130, render: (s: Inventar['stare']) => (s === 'Deschis' ? <Tag color="gold">în numărare</Tag> : <Tag color="green">finalizat</Tag>) },
          { title: 'Numărate', width: 110, align: 'right', render: (_, i) => `${i.numarate} / ${i.linii.length}` },
          { title: 'Plusuri', dataIndex: 'valoarePlusuri', width: 120, align: 'right', render: bani },
          { title: 'Minusuri', dataIndex: 'valoareMinusuri', width: 120, align: 'right', render: bani },
        ]}
      />
      <Modal open={nou} title="Inventar nou" okText="Deschide inventarul" cancelText="Renunță" onCancel={() => setNou(false)} onOk={() => form.submit()}
        confirmLoading={deschide.isPending} destroyOnHidden>
        <Form form={form} layout="vertical" name="inventarNou" initialValues={{ data: dayjs() }} onFinish={async (v) => {
          try {
            const inv = await deschide.mutateAsync({ gestiuneId: v.gestiuneId, data: v.data.format('YYYY-MM-DD'), comisie: v.comisie?.trim() || null })
            message.success(`${inv.numar} deschis: ${inv.linii.length} materiale de numărat.`)
            setNou(false)
            setDeschisId(inv.id)
          } catch (e) {
            message.error((e as Error).message)
          }
        }}>
          <Form.Item name="gestiuneId" label="Gestiunea" rules={[{ required: true, message: 'Alege gestiunea.' }]}>
            <Select showSearch={{ optionFilterProp: 'label' }} options={(gestiuni.data ?? []).map((g) => ({ value: g.id, label: `${g.cod} — ${g.denumire}` }))} />
          </Form.Item>
          <Form.Item name="data" label="Data inventarului" rules={[{ required: true }]}>
            <DatePicker format="DD.MM.YYYY" disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
          </Form.Item>
          <Form.Item name="comisie" label="Comisia de inventariere">
            <Input maxLength={500} placeholder="Numele membrilor comisiei" />
          </Form.Item>
          <Alert type="warning" showIcon title="Cât timp inventarul e deschis, gestiunea nu mai acceptă recepții, transferuri sau consumuri." />
        </Form>
      </Modal>
      {deschisCurent && <FisaInventar key={`${deschisCurent.id}-${deschisCurent.stare}-${deschisCurent.numarate}-${deschisCurent.linii.length}`} inventar={deschisCurent} onInchide={() => setDeschisId(undefined)} poateEdita={poateEdita} poateAproba={poateAproba} />}
    </Card>
  )
}
