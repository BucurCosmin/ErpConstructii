import { CheckOutlined, DeleteOutlined, EditOutlined, LinkOutlined, PlusOutlined } from '@ant-design/icons'
import { App, AutoComplete, Button, Checkbox, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { instrumentePlata, propunereFacturaFisa, tipuriFacturaContract, useFacturaFisa, usePlataContract } from '../../api/contracte'
import type { Contract, FacturaFisa, FacturaFisaCerere, InstrumentPlata, PlataContract, SituatieMembru, TipFacturaContract } from '../../api/contracte'
import { LinkFactura } from '../../componente/LinkFactura'
import { NumarRo } from '../../componente/NumarRo'
import { formatData, formatLei } from '../../util/format'

const lei = (v: number | null | undefined) => (v === null || v === undefined ? '—' : formatLei(v))

/** Situația fișei pe fiecare membru al asocierii și total (ca foaia „Facturi / plăți” + reconcilierea). */
function SituatieMembri({ c }: { c: Contract }) {
  const s = c.situatieFisa
  if (!s) return null
  const randuri = s.membri.length > 1 ? [...s.membri, s.total] : [s.total]
  const coloane: TableColumnsType<SituatieMembru> = [
    { title: 'Antreprenor', dataIndex: 'membru', width: 220, fixed: 'left', render: (v: string, r) => <Typography.Text strong={v === 'Total'}>{v}{r.esteFirmaActiva && s.membri.length > 1 ? <Tag style={{ marginInlineStart: 6 }}>firma</Tag> : null}</Typography.Text> },
    { title: `Valoare (${c.moneda})`, dataIndex: 'valoareActuala', width: 140, align: 'right', render: lei },
    { title: 'Lucrări facturate', width: 160, align: 'right', render: (_, r) => <>{lei(r.lucrariFacturate)} <Typography.Text type="secondary">({r.procentExecutat.toLocaleString('ro-RO')}%)</Typography.Text></> },
    { title: 'Rest de executat', dataIndex: 'restDeExecutat', width: 140, align: 'right', render: lei },
    { title: 'Avans facturat', dataIndex: 'avansFacturat', width: 130, align: 'right', render: lei },
    { title: 'Avans recuperat', dataIndex: 'avansRecuperat', width: 130, align: 'right', render: lei },
    {
      title: <Tooltip title="Facturat − recuperat prin storno; negativ = s-a stornat mai mult decât s-a facturat ca avans în fișă (ex. factura de avans lipsește)">Avans rămas</Tooltip>,
      dataIndex: 'avansRamas', width: 130, align: 'right', render: (v: number) => <Typography.Text type={v < 0 ? 'danger' : undefined}>{lei(v)}</Typography.Text>,
    },
    { title: 'Facturat RON (cu TVA)', dataIndex: 'facturatTotalRon', width: 160, align: 'right', render: lei },
    { title: 'Plătit confirmat', dataIndex: 'platitConfirmat', width: 140, align: 'right', render: lei },
    { title: <Tooltip title="Instrumente emise (ex. bilet la ordin), neîncasate încă">Emis neconfirmat</Tooltip>, dataIndex: 'emisNeconfirmat', width: 140, align: 'right', render: lei },
    { title: 'Sold RON', dataIndex: 'sold', width: 140, align: 'right', render: (v: number) => <Typography.Text strong>{lei(v)}</Typography.Text> },
  ]
  return <Table<SituatieMembru> className="tabel-compact" size="small" pagination={false} rowKey="membru" dataSource={randuri} columns={coloane} scroll={{ x: 1700 }}
    onRow={(r) => ({ style: r.membru === 'Total' ? { background: '#fafafa' } : undefined })} />
}

interface ValoriFactura extends Omit<FacturaFisaCerere, 'data' | 'scadenta'> {
  data: Dayjs
  scadenta?: Dayjs | null
}

/** Facturile din fișă, facturile legate încă neadăugate și plățile. */
export function FacturiPlatiContract({ c, poateEdita }: { c: Contract; poateEdita: boolean }) {
  const { message } = App.useApp()
  const salveazaFactura = useFacturaFisa()
  const salveazaPlata = usePlataContract()
  const [formFactura] = Form.useForm<ValoriFactura>()
  const [formPlata] = Form.useForm<Omit<PlataContract, 'id' | 'ordine' | 'data' | 'scadenta'> & { data: Dayjs; scadenta?: Dayjs | null }>()
  const [factura, setFactura] = useState<{ id?: string } | null>(null)
  const [plata, setPlata] = useState<{ id?: string } | null>(null)
  const [deAdaugat, setDeAdaugat] = useState<{ facturaId: string; numar: string } | null>(null)
  const [curs, setCurs] = useState<number | null>(null)
  const tip = Form.useWatch('tip', formFactura) as TipFacturaContract | undefined

  const facturiFisa = c.facturiFisa ?? []
  const plati = c.plati ?? []
  const membri = (c.conditii?.membri.length ? c.conditii.membri.map((m) => m.denumire) : ['Firma'])
  const inFisa = new Set(facturiFisa.map((f) => f.facturaId).filter(Boolean))
  const legateNeadaugate = (c.facturi ?? []).filter((f) => !inFisa.has(f.id))
  const eurContract = c.moneda !== 'RON'
  const eroare = (e: unknown) => message.error((e as Error).message)

  const deschideFactura = (f?: Partial<FacturaFisa>, id?: string) => {
    formFactura.resetFields()
    formFactura.setFieldsValue({
      membru: f?.membru ?? membri[0], facturaId: f?.facturaId ?? null, numar: f?.numar ?? '', data: f?.data ? dayjs(f.data) : dayjs(),
      tip: f?.tip ?? 'Lucrari', transaAvans: f?.transaAvans ?? null, situatiePlata: f?.situatiePlata ?? null, lunaExecutie: f?.lunaExecutie ?? null,
      valoare: f?.valoare ?? 0, stornoAvans: f?.stornoAvans ?? 0, curs: f?.curs ?? null, netRon: f?.netRon ?? 0, tvaRon: f?.tvaRon ?? 0, totalRon: f?.totalRon ?? 0,
      scadenta: f?.scadenta ? dayjs(f.scadenta) : null, observatii: f?.observatii ?? null,
    })
    setFactura({ id })
  }

  const adaugaDinFacturi = async () => {
    if (!deAdaugat) return
    try {
      const p = await propunereFacturaFisa(c.proiectId, c.id, deAdaugat.facturaId, curs)
      setDeAdaugat(null)
      deschideFactura({ ...p, data: p.data, scadenta: p.scadenta })
    } catch (e) {
      eroare(e)
    }
  }

  const trimiteFactura = async () => {
    const v = await formFactura.validateFields()
    try {
      await salveazaFactura.mutateAsync({
        proiectId: c.proiectId, id: c.id, randId: factura?.id,
        date: { ...v, data: v.data.format('YYYY-MM-DD'), scadenta: v.scadenta?.format('YYYY-MM-DD') ?? null, valoare: Number(v.valoare ?? 0), stornoAvans: Number(v.stornoAvans ?? 0),
          netRon: Number(v.netRon ?? 0), tvaRon: Number(v.tvaRon ?? 0), totalRon: Number(v.totalRon ?? 0) },
      })
      setFactura(null)
    } catch (e) {
      eroare(e)
    }
  }

  const deschidePlata = (p?: PlataContract, facturaContractId?: string) => {
    formPlata.resetFields()
    const f = facturiFisa.find((x) => x.id === facturaContractId)
    formPlata.setFieldsValue(p
      ? { ...p, data: dayjs(p.data), scadenta: p.scadenta ? dayjs(p.scadenta) : null }
      : { facturaContractId: facturaContractId ?? null, data: dayjs(), sumaRon: f ? f.sold : 0, instrument: 'OrdinDePlata', confirmata: true, numar: null, observatii: null })
    setPlata({ id: p?.id })
  }

  const trimitePlata = async () => {
    const v = await formPlata.validateFields()
    try {
      await salveazaPlata.mutateAsync({ proiectId: c.proiectId, id: c.id, randId: plata?.id,
        date: { ...v, data: v.data.format('YYYY-MM-DD'), scadenta: v.scadenta?.format('YYYY-MM-DD') ?? null, sumaRon: Number(v.sumaRon) } })
      setPlata(null)
    } catch (e) {
      eroare(e)
    }
  }

  const coloaneFacturi: TableColumnsType<FacturaFisa> = [
    { title: 'Antreprenor', dataIndex: 'membru', width: 150, ellipsis: true },
    { title: 'Factura', width: 130, render: (_, f) => (f.facturaId ? <LinkFactura id={f.facturaId} numar={f.numar} /> : f.numar) },
    { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
    { title: 'Tip', width: 100, render: (_, f) => `${tipuriFacturaContract[f.tip]}${f.transaAvans ? ` T${f.transaAvans}` : ''}` },
    { title: 'Situație plată', dataIndex: 'situatiePlata', width: 120, ellipsis: true },
    { title: `${eurContract ? 'Lucrări / avans' : 'Valoare'} (${c.moneda})`, dataIndex: 'valoare', width: 130, align: 'right', render: lei },
    { title: `Storno avans (${c.moneda})`, dataIndex: 'stornoAvans', width: 120, align: 'right', render: (v: number) => (v ? lei(v) : '') },
    ...(eurContract ? [{ title: 'Curs', dataIndex: 'curs', width: 75, align: 'right' as const, render: (v: number | null) => v?.toLocaleString('ro-RO', { maximumFractionDigits: 4 }) ?? '' }] : []),
    { title: 'Net RON', dataIndex: 'netRon', width: 120, align: 'right', render: lei },
    { title: 'TVA RON', dataIndex: 'tvaRon', width: 105, align: 'right', render: lei },
    { title: 'Total RON', dataIndex: 'totalRon', width: 125, align: 'right', render: lei },
    {
      title: 'Sold', dataIndex: 'sold', width: 150, align: 'right',
      render: (v: number, f) => {
        if (v <= 0.01) return <Tag color="green">plătită</Tag>
        const scadenta = f.scadenta ?? (c.termenPlataZile !== null ? dayjs(f.data).add(c.termenPlataZile, 'day').format('YYYY-MM-DD') : null)
        const depasita = scadenta && dayjs(scadenta).isBefore(dayjs(), 'day')
        return <Tooltip title={scadenta ? `Scadență ${formatData(scadenta)}` : undefined}><Typography.Text type={depasita ? 'danger' : undefined}>{lei(v)}{depasita ? ' ⚠' : ''}</Typography.Text></Tooltip>
      },
    },
    ...(poateEdita ? [{
      title: '', key: 'a', width: 100, fixed: 'right' as const,
      render: (_: unknown, f: FacturaFisa) => (
        <Space size={0}>
          <Tooltip title="Plată pe factură"><Button size="small" type="text" icon={<PlusOutlined />} onClick={() => deschidePlata(undefined, f.id)} /></Tooltip>
          <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschideFactura(f, f.id)} />
          <Popconfirm title="Scoți factura din fișă?" description="Plățile ei rămân, neatribuite." okText="Scoate" okButtonProps={{ danger: true }} cancelText="Nu"
            onConfirm={() => salveazaFactura.mutateAsync({ proiectId: c.proiectId, id: c.id, randId: f.id, sterge: true }).catch(eroare)}>
            <Button size="small" type="text" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    }] : []),
  ]

  const t = c.situatieFisa?.total
  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <SituatieMembri c={c} />

      <Table<FacturaFisa> className="tabel-compact" size="small" pagination={false} rowKey="id" dataSource={facturiFisa} columns={coloaneFacturi} scroll={{ x: 1600 }}
        locale={{ emptyText: 'Nicio factură în fișă: adaugă facturile legate din modulul Facturi (mai jos) sau facturile celuilalt membru al asocierii.' }}
        title={() => (
          <Space>
            <Typography.Text strong>Facturi și situații de plată</Typography.Text>
            {poateEdita && <Button size="small" icon={<PlusOutlined />} onClick={() => deschideFactura()}>Factură (introdusă manual)</Button>}
          </Space>
        )}
        summary={() => t && facturiFisa.length > 0 ? (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={5}><b>Total</b></Table.Summary.Cell>
            <Table.Summary.Cell index={5} align="right"><b>{lei(t.lucrariFacturate + t.avansFacturat)}</b></Table.Summary.Cell>
            <Table.Summary.Cell index={6} align="right"><b>{lei(t.avansRecuperat)}</b></Table.Summary.Cell>
            {eurContract && <Table.Summary.Cell index={7} />}
            <Table.Summary.Cell index={8} align="right"><b>{lei(t.facturatNetRon)}</b></Table.Summary.Cell>
            <Table.Summary.Cell index={9} align="right"><b>{lei(t.facturatTvaRon)}</b></Table.Summary.Cell>
            <Table.Summary.Cell index={10} align="right"><b>{lei(t.facturatTotalRon)}</b></Table.Summary.Cell>
            <Table.Summary.Cell index={11} align="right"><b>{lei(t.sold)}</b></Table.Summary.Cell>
          </Table.Summary.Row>
        ) : null} />

      {legateNeadaugate.length > 0 && (
        <Table size="small" pagination={false} rowKey="id" dataSource={legateNeadaugate}
          title={() => <Typography.Text strong>Facturi legate de contract în modulul Facturi, încă neadăugate în fișă</Typography.Text>}
          columns={[
            { title: 'Factura', render: (_, f) => <LinkFactura id={f.id} numar={f.numar} /> },
            { title: 'Data', dataIndex: 'data', width: 110, render: formatData },
            { title: 'Fără TVA', width: 160, align: 'right', render: (_, f) => `${formatLei(f.totalFaraTva)} ${f.moneda}` },
            ...(poateEdita ? [{ title: '', width: 150, render: (_: unknown, f: { id: string; numar: string }) => (
              <Button size="small" icon={<LinkOutlined />} onClick={() => { setCurs(null); setDeAdaugat({ facturaId: f.id, numar: f.numar }) }}>Adaugă în fișă</Button>
            ) }] : []),
          ]} />
      )}

      <Table<PlataContract> className="tabel-compact" size="small" pagination={false} rowKey="id" dataSource={plati}
        locale={{ emptyText: 'Nicio plată înregistrată (câmpul gol nu înseamnă neplată: se completează din extrase / OP).' }}
        title={() => (
          <Space>
            <Typography.Text strong>Plăți</Typography.Text>
            {poateEdita && <Button size="small" icon={<PlusOutlined />} onClick={() => deschidePlata()}>Plată</Button>}
          </Space>
        )}
        columns={[
          { title: 'Data', dataIndex: 'data', width: 95, render: formatData },
          { title: 'Factura', width: 160, render: (_, p) => facturiFisa.find((f) => f.id === p.facturaContractId)?.numar ?? <Typography.Text type="secondary">neatribuită</Typography.Text> },
          { title: 'Instrument', dataIndex: 'instrument', width: 130, render: (v: InstrumentPlata) => instrumentePlata[v] },
          { title: 'Nr.', dataIndex: 'numar', width: 120 },
          { title: 'Sumă RON', dataIndex: 'sumaRon', width: 130, align: 'right', render: lei },
          { title: 'Scadență', dataIndex: 'scadenta', width: 100, render: formatData },
          { title: 'Stare', dataIndex: 'confirmata', width: 120, render: (v: boolean) => (v ? <Tag color="green">confirmată</Tag> : <Tag color="gold">emisă, neconfirmată</Tag>) },
          { title: 'Observații', dataIndex: 'observatii', ellipsis: true },
          ...(poateEdita ? [{
            title: '', key: 'a', width: 100,
            render: (_: unknown, p: PlataContract) => (
              <Space size={0}>
                {!p.confirmata && (
                  <Tooltip title="Încasată / dovedită: confirmă plata">
                    <Button size="small" type="text" icon={<CheckOutlined />} onClick={() => salveazaPlata.mutateAsync({ proiectId: c.proiectId, id: c.id, randId: p.id, date: { ...p, confirmata: true } }).catch(eroare)} />
                  </Tooltip>
                )}
                <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschidePlata(p)} />
                <Popconfirm title="Ștergi plata?" okText="Șterge" okButtonProps={{ danger: true }} cancelText="Nu"
                  onConfirm={() => salveazaPlata.mutateAsync({ proiectId: c.proiectId, id: c.id, randId: p.id, sterge: true }).catch(eroare)}>
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                </Popconfirm>
              </Space>
            ),
          }] : []),
        ]} />

      <Modal open={!!deAdaugat} title={`Adaugă în fișă factura ${deAdaugat?.numar ?? ''}`} okText="Continuă" cancelText="Renunță" onOk={() => void adaugaDinFacturi()} onCancel={() => setDeAdaugat(null)} destroyOnHidden>
        {eurContract ? (
          <Space orientation="vertical" style={{ width: '100%' }}>
            <Typography.Text>Cursul BNR scris pe factură (lei / {c.moneda}), ca liniile facturii să fie aduse în moneda contractului:</Typography.Text>
            <InputNumber<number> min={0} step={0.0001} decimalSeparator="," style={{ width: 200 }} value={curs} onChange={(v) => setCurs(v)} placeholder="ex. 5,2052" />
            <Typography.Text type="secondary">Fără curs, valorile în {c.moneda} rămân de completat. Liniile negative (stornări) devin avansul recuperat.</Typography.Text>
          </Space>
        ) : <Typography.Text>Rândul se propune din liniile facturii; verifică-l înainte de salvare.</Typography.Text>}
      </Modal>

      <Modal open={!!factura} title={factura?.id ? 'Modifică factura din fișă' : 'Factură în fișa contractului'} okText="Salvează" cancelText="Renunță" width={820}
        confirmLoading={salveazaFactura.isPending} onOk={() => void trimiteFactura()} onCancel={() => setFactura(null)} destroyOnHidden forceRender>
        <Form form={formFactura} layout="vertical">
          <Form.Item name="facturaId" hidden><Input /></Form.Item>
          <Row gutter={12}>
            <Col xs={24} md={10}><Form.Item name="membru" label="Antreprenorul facturat" rules={[{ required: true, whitespace: true }]}><AutoComplete options={membri.map((m) => ({ value: m }))} /></Form.Item></Col>
            <Col xs={12} md={7}><Form.Item name="numar" label="Nr. factură" rules={[{ required: true, whitespace: true }]}><Input maxLength={50} /></Form.Item></Col>
            <Col xs={12} md={7}><Form.Item name="data" label="Data" rules={[{ required: true }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={6}><Form.Item name="tip" label="Tip"><Select options={Object.entries(tipuriFacturaContract).map(([value, label]) => ({ value, label }))} /></Form.Item></Col>
            <Col xs={12} md={4}><Form.Item name="transaAvans" label={tip === 'Avans' ? 'Tranșa' : 'Tranșa stornată'}><Select allowClear options={(c.conditii?.transeAvans ?? []).map((x) => ({ value: x.numar, label: `T${x.numar}` }))} /></Form.Item></Col>
            <Col xs={12} md={7}><Form.Item name="situatiePlata" label="Situație de plată"><Input maxLength={100} placeholder="ex. 3034/10.12.2025" /></Form.Item></Col>
            <Col xs={12} md={7}><Form.Item name="lunaExecutie" label="Luna execuției"><Input maxLength={50} placeholder="ex. Noiembrie 2025" /></Form.Item></Col>
            <Col xs={12} md={8}><Form.Item name="valoare" label={`${tip === 'Avans' ? 'Avans' : 'Lucrări / livrare (brut)'} (${c.moneda})`}><NumarRo style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={8}><Form.Item name="stornoAvans" label={`Storno avans (${c.moneda})`}><NumarRo min={0} style={{ width: '100%' }} disabled={tip === 'Avans'} /></Form.Item></Col>
            {eurContract && <Col xs={12} md={8}><Form.Item name="curs" label="Curs BNR de pe factură"><NumarRo min={0} style={{ width: '100%' }} /></Form.Item></Col>}
            <Col xs={8}><Form.Item name="netRon" label="Net RON (fără TVA)"><NumarRo style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={8}><Form.Item name="tvaRon" label="TVA RON"><NumarRo style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={8}><Form.Item name="totalRon" label="Total RON" extra="Gol = net + TVA."><NumarRo style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12} md={8}><Form.Item name="scadenta" label="Scadența" extra={c.termenPlataZile !== null ? `Gol = data + ${c.termenPlataZile} zile.` : undefined}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={24} md={16}><Form.Item name="observatii" label="Observații"><Input maxLength={2000} /></Form.Item></Col>
          </Row>
        </Form>
      </Modal>

      <Modal open={!!plata} title={plata?.id ? 'Modifică plata' : 'Plată către subantreprenor'} okText="Salvează" cancelText="Renunță" width={640}
        confirmLoading={salveazaPlata.isPending} onOk={() => void trimitePlata()} onCancel={() => setPlata(null)} destroyOnHidden forceRender>
        <Form form={formPlata} layout="vertical">
          <Row gutter={12}>
            <Col xs={24}><Form.Item name="facturaContractId" label="Factura"><Select allowClear placeholder="neatribuită" options={facturiFisa.map((f) => ({ value: f.id, label: `${f.numar} · ${f.membru} · sold ${formatLei(f.sold)}` }))} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="data" label="Data" rules={[{ required: true }]}><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="sumaRon" label="Suma (RON)" rules={[{ required: true }]}><NumarRo min={0} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="instrument" label="Instrument"><Select options={Object.entries(instrumentePlata).map(([value, label]) => ({ value, label }))} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="numar" label="Nr. (OP / BO / CEC)"><Input maxLength={100} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="scadenta" label="Scadența instrumentului"><DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={12}><Form.Item name="confirmata" valuePropName="checked" label=" " extra="Debifat = emis (ex. bilet la ordin), încă neîncasat."><Checkbox>Plată confirmată</Checkbox></Form.Item></Col>
            <Col xs={24}><Form.Item name="observatii" label="Observații"><Input maxLength={2000} /></Form.Item></Col>
          </Row>
        </Form>
      </Modal>
    </Space>
  )
}
