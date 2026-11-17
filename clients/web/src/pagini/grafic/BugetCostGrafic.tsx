import { FileExcelOutlined, FilePdfOutlined, WarningOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, InputNumber, Row, Segmented, Skeleton, Space, Statistic, Switch, Table, Tooltip, Typography, theme } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { bugetGraficExcel, bugetGraficPdf, stadii, useBugetGrafic } from '../../api/grafic'
import type { RandBugetGrafic } from '../../api/grafic'
import { formatLei } from '../../util/format'
import { descarcaFisier } from '../../util/pdf'

const procent = (v: number | null) => (v === null ? '' : `${v.toLocaleString('ro-RO', { maximumFractionDigits: 1 })}%`)

/**
 * Valoarea din grafic față de costurile ERP alocate (Grafic → Buget vs cost, D-57, etapa 3): pe arborele WBS, valoarea
 * planificată și executată (din procentul de execuție al graficului), costul ERP (facturi + consumuri, la cursul BNR),
 * abaterea, indicele de cost, prognoza la terminare și alertele; export Excel și PDF.
 */
export function BugetCostGrafic({ proiectId, graficId, laActivitate }: { proiectId: string; graficId: string; laActivitate: (cod: string) => void }) {
  const { message } = App.useApp()
  const { token } = theme.useToken()
  const [prag, setPrag] = useState(10)
  const [nivel, setNivel] = useState<number | null>(2)
  const [doarAlerte, setDoarAlerte] = useState(false)
  const [doarCuValori, setDoarCuValori] = useState(true)
  const { data, isLoading, error } = useBugetGrafic(proiectId, graficId, prag / 100, true)
  const m = data?.moneda ?? ''

  const randuri = useMemo(() => (data?.raport.randuri ?? []).filter((r) =>
    (nivel === null || (r.esteNod && r.adancime < nivel))
    && (!doarAlerte || r.alerta !== null)
    && (!doarCuValori || r.valoarePlanificata !== 0 || r.costErp !== 0)), [data, nivel, doarAlerte, doarCuValori])

  const descarca = async (tip: 'xlsx' | 'pdf') => {
    try {
      const blob = tip === 'xlsx' ? await bugetGraficExcel(proiectId, graficId, prag / 100) : await bugetGraficPdf(proiectId, graficId, prag / 100, nivel, doarAlerte)
      descarcaFisier(blob, `Buget_cost.${tip}`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  if (error) return <Alert type="error" showIcon title={(error as Error).message} />
  if (isLoading || !data) return <Skeleton active />
  const t = data.raport.total
  const alerte = data.raport.randuri.filter((r) => r.alerta).length

  const coloane: TableColumnsType<RandBugetGrafic> = [
    {
      title: 'Cod / WBS', dataIndex: 'cod', width: 200, fixed: 'left', ellipsis: true,
      render: (c: string, r) => (r.esteNod ? <b>{c}</b> : <Typography.Link onClick={() => laActivitate(r.cheie)}>{c}</Typography.Link>),
    },
    {
      title: 'Denumire', dataIndex: 'denumire', width: 380, ellipsis: { showTitle: false },
      render: (d: string, r) => (
        <Tooltip title={d} placement="topLeft">
          <span style={{ paddingLeft: r.adancime * 14, fontWeight: r.esteNod ? 600 : 400 }}>{d}</span>
          {r.stadiu && <Typography.Text type="secondary" style={{ fontSize: 11 }}> · {stadii[r.stadiu].eticheta}</Typography.Text>}
        </Tooltip>
      ),
    },
    { title: `Planificat (${m})`, dataIndex: 'valoarePlanificata', width: 130, className: 'numeric', render: (v: number) => (v ? formatLei(v) : '') },
    { title: 'Exec. %', dataIndex: 'procent', width: 75, className: 'numeric', render: (v: number, r) => (r.valoarePlanificata ? procent(v) : '') },
    { title: `Executat (${m})`, dataIndex: 'valoareExecutata', width: 130, className: 'numeric', render: (v: number) => (v ? formatLei(v) : '') },
    {
      title: <Tooltip title="Facturi (fără materialele de stoc) și bonuri de consum alocate, la cursul BNR din data documentelor">{`Cost ERP (${m})`}</Tooltip>,
      dataIndex: 'costErp', width: 130, className: 'numeric',
      render: (v: number, r) => (v ? <Tooltip title={`${formatLei(r.costErpLei)} lei · ${r.documente} documente${r.faraCurs ? ` · ${r.faraCurs} alocări fără curs BNR` : ''}`}>{formatLei(v)}</Tooltip> : ''),
    },
    {
      title: <Tooltip title="Valoarea executată − costul ERP (negativ = cost peste valoarea executată)">{`Abatere (${m})`}</Tooltip>,
      dataIndex: 'abatere', width: 130, className: 'numeric',
      render: (v: number, r) => (r.costErp || r.valoareExecutata ? <Typography.Text type={v < 0 ? 'danger' : undefined}>{formatLei(v)}</Typography.Text> : ''),
    },
    {
      title: <Tooltip title="Valoarea executată / costul ERP: sub 1 = depășire">Indice cost</Tooltip>, dataIndex: 'indiceCost', width: 90, className: 'numeric',
      render: (v: number | null) => (v === null ? '' : <Typography.Text type={v < 1 ? 'danger' : 'success'}>{v.toLocaleString('ro-RO', { maximumFractionDigits: 3 })}</Typography.Text>),
    },
    { title: <Tooltip title="Costul ERP / valoarea planificată">Consumat</Tooltip>, dataIndex: 'procentConsumat', width: 85, className: 'numeric', render: procent },
    {
      title: <Tooltip title="Costul ERP + valoarea rămasă de executat (restul la valoarea planificată)">{`Prognoză (${m})`}</Tooltip>,
      dataIndex: 'prognoza', width: 130, className: 'numeric', render: (v: number) => (v ? formatLei(v) : ''),
    },
    {
      title: 'Alertă', dataIndex: 'alerta', width: 260, ellipsis: true,
      render: (a: string | null) => (a ? <Tooltip title={a}><Typography.Text type="danger"><WarningOutlined /> {a}</Typography.Text></Tooltip> : ''),
    },
  ]

  return (
    <Space orientation="vertical" size={12} style={{ width: '100%' }}>
      <Row gutter={[12, 12]}>
        <Col xs={12} md={4}><Card size="small"><Statistic title={`Valoare planificată (${m})`} value={formatLei(t.valoarePlanificata)} styles={{ content: { fontSize: 18 } }} /></Card></Col>
        <Col xs={12} md={4}>
          <Card size="small"><Statistic title={`Valoare executată (${procent(t.procent)})`} value={formatLei(t.valoareExecutata)} styles={{ content: { fontSize: 18 } }} /></Card>
        </Col>
        <Col xs={12} md={4}>
          <Card size="small">
            <Statistic title={`Cost ERP (${m})`} value={formatLei(t.costErp)} styles={{ content: { fontSize: 18 } }}
              suffix={<Typography.Text type="secondary" style={{ fontSize: 12 }}>{formatLei(t.costErpLei)} lei</Typography.Text>} />
          </Card>
        </Col>
        <Col xs={12} md={4}>
          <Card size="small"><Statistic title={`Abatere (${m})`} value={formatLei(t.abatere)} styles={{ content: { fontSize: 18, color: t.abatere < 0 ? token.colorError : token.colorSuccess } }} /></Card>
        </Col>
        <Col xs={12} md={3}>
          <Card size="small"><Statistic title="Indice cost" value={t.indiceCost === null ? '—' : t.indiceCost.toLocaleString('ro-RO', { maximumFractionDigits: 3 })} styles={{ content: { fontSize: 18 } }} /></Card>
        </Col>
        <Col xs={12} md={5}><Card size="small"><Statistic title={`Prognoză la terminare (${m})`} value={formatLei(t.prognoza)} styles={{ content: { fontSize: 18 } }} /></Card></Col>
      </Row>

      {t.costErp === 0 && (
        <Alert type="info" showIcon title="Nu există încă costuri ERP alocate pe grafic: alocă facturile și bonurile de consum în „Alocare costuri”." />
      )}
      {data.raport.costFaraTinta.lei !== 0 && (
        <Alert type="warning" showIcon
          title={`${formatLei(data.raport.costFaraTinta.monedaGrafic ?? 0)} ${m} (${formatLei(data.raport.costFaraTinta.lei)} lei) sunt alocați pe activități / noduri care nu mai sunt în actualizarea curentă: intră doar în total. Realocă-i din „Alocare costuri”.`} />
      )}

      <Space wrap>
        <Segmented<string> value={nivel === null ? 'tot' : String(nivel)} onChange={(v) => setNivel(v === 'tot' ? null : Number(v))}
          options={[{ value: '1', label: 'Nivel 1' }, { value: '2', label: 'Nivel 2' }, { value: '3', label: 'Nivel 3' }, { value: '4', label: 'Nivel 4' }, { value: 'tot', label: 'Cu activitățile' }]} />
        <Space size={4}><Switch size="small" checked={doarAlerte} onChange={setDoarAlerte} /> Doar alertele ({alerte})</Space>
        <Space size={4}><Switch size="small" checked={doarCuValori} onChange={setDoarCuValori} /> Fără rândurile goale</Space>
        <Space size={4}>
          <Tooltip title="Alertă când costul ERP depășește valoarea executată cu mai mult de atât">Prag alertă</Tooltip>
          <InputNumber size="small" min={1} max={500} value={prag} onChange={(v) => setPrag(Number(v ?? 10))} suffix="%" style={{ width: 80 }} />
        </Space>
        <Button icon={<FileExcelOutlined />} onClick={() => void descarca('xlsx')}>Excel</Button>
        <Tooltip title="PDF cu selecția de nivel și alerte de mai sus"><Button icon={<FilePdfOutlined />} onClick={() => void descarca('pdf')}>PDF</Button></Tooltip>
      </Space>

      <Table<RandBugetGrafic> className="tabel-compact" size="small" rowKey={(r) => `${r.esteNod ? 'w' : 'a'}:${r.cheie}`} dataSource={randuri} columns={coloane}
        pagination={randuri.length > 300 ? { pageSize: 300, size: 'small', showSizeChanger: false } : false}
        scroll={{ x: 1800, y: 'calc(100vh - 360px)' }} sticky
        onRow={(r) => ({ style: { background: r.alerta ? token.colorErrorBg : r.esteNod && r.adancime === 0 ? token.colorFillQuaternary : undefined } })}
        summary={() => (
          <Table.Summary fixed="top">
            <Table.Summary.Row>
              <Table.Summary.Cell index={0} colSpan={2}><b>Total grafic</b></Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right"><b>{formatLei(t.valoarePlanificata)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={3} align="right"><b>{procent(t.procent)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={4} align="right"><b>{formatLei(t.valoareExecutata)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={5} align="right"><b>{formatLei(t.costErp)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={6} align="right"><b>{formatLei(t.abatere)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={7} align="right"><b>{t.indiceCost?.toLocaleString('ro-RO', { maximumFractionDigits: 3 }) ?? ''}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={8} align="right"><b>{procent(t.procentConsumat)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={9} align="right"><b>{formatLei(t.prognoza)}</b></Table.Summary.Cell>
              <Table.Summary.Cell index={10} />
            </Table.Summary.Row>
          </Table.Summary>
        )} />
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        Valoarea executată = valoarea planificată din grafic × procentul de execuție. Costurile alocate direct pe un nod WBS apar la nod (și în sus), nu pe activitățile lui.
      </Typography.Text>
    </Space>
  )
}
