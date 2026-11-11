import { EditOutlined, EnvironmentOutlined, InboxOutlined, RollbackOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Descriptions, Empty, Popconfirm, Progress, Result, Row, Skeleton, Table, Tabs, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { EroareApi } from '../../api/client'
import { useDevize, usePermisiuni, useProiect } from '../../api/interogari'
import type { VersiuneDeviz } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData, stariProiect, stariVersiune, timpScurs, tipuriVersiune } from '../../util/format'
import { ImportDeviz } from '../devize/ImportDeviz'
import { useArhiveazaProiect, useReactiveazaProiect } from '../../api/proiecte'
import { EditareProiect } from './EditareProiect'
import { SantiereProiect } from './SantiereProiect'
import { ListaContracte } from '../contracte/ListaContracte'

export function DetaliiProiect() {
  const { proiectId = '' } = useParams()
  const [parametri, setParametri] = useSearchParams()
  const proiect = useProiect(proiectId)
  const devize = useDevize(proiectId)
  const permisiuni = usePermisiuni()
  const navigheaza = useNavigate()
  const [importDeschis, setImportDeschis] = useState(false)
  const [editareDeschisa, setEditareDeschisa] = useState(false)
  const { message } = App.useApp()
  const arhiveaza = useArhiveazaProiect()
  const reactiveaza = useReactiveazaProiect()
  const schimbaArhivarea = async (arhivare: boolean) => {
    try {
      await (arhivare ? arhiveaza : reactiveaza).mutateAsync(proiectId)
      message.success(arhivare ? 'Proiectul a fost arhivat: nu mai apare în liste. Îl găsești în Proiecte → Arhivate.' : 'Proiectul a fost reactivat.')
    } catch (e) {
      message.error((e as Error).message)
    }
  }
  const tab = parametri.get('tab') ?? 'general'

  if (proiect.error instanceof EroareApi && proiect.error.status === 404) {
    return <Result status="404" title="Proiect inexistent" extra={<Link to="/proiecte">Înapoi la proiecte</Link>} />
  }

  const p = proiect.data
  const scurs = p ? timpScurs(p.dataInceput, p.termenContractual) : null
  const poateImporta = permisiuni.data?.includes('ofertare.creare') ?? false
  const poateEdita = permisiuni.data?.includes('proiecte.editare') ?? false

  return (
    <>
      <AntetPagina
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Proiecte', la: '/proiecte' }, { titlu: p?.cod ?? '…' }]}
        titlu={
          p ? (
            <>
              {p.cod}{' '}
              <Tag color={stariProiect[p.stare].culoare} style={{ verticalAlign: 'middle', fontSize: 13 }}>
                {stariProiect[p.stare].eticheta}
              </Tag>
            </>
          ) : (
            <Skeleton.Input active />
          )
        }
        subtitlu={p?.denumire}
        actiuni={
          <>
            <Button icon={<EditOutlined />} disabled={!p || !poateEdita} onClick={() => setEditareDeschisa(true)}
              title={poateEdita ? undefined : 'Doar administratorul (drept de editare pe proiecte) editează proiectul'}>
              Editează
            </Button>
            {p && poateEdita && (p.arhivat
              ? <Button icon={<RollbackOutlined />} loading={reactiveaza.isPending} onClick={() => void schimbaArhivarea(false)}>Reactivează</Button>
              : (
                <Popconfirm title={`Arhivezi proiectul ${p.cod}?`} okText="Arhivează" cancelText="Nu" onConfirm={() => schimbaArhivarea(true)}
                  description="Dispare din liste și din alegeri și nu mai primește documente noi (doar returul materialelor rămase în șantier). Documentele și rapoartele rămân; îl reactivezi oricând.">
                  <Button icon={<InboxOutlined />} loading={arhiveaza.isPending}>Arhivează</Button>
                </Popconfirm>
              ))}
            <Button type="primary" icon={<UploadOutlined />} disabled={!poateImporta || p?.arhivat} onClick={() => setImportDeschis(true)}>
              Importă deviz
            </Button>
          </>
        }
      />

      {proiect.error && !(proiect.error instanceof EroareApi && proiect.error.status === 404) && (
        <Alert type="error" showIcon title={(proiect.error as Error).message} style={{ marginBottom: 16 }} />
      )}

      {p?.arhivat && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }}
          title={`Proiect arhivat${p.arhivatLa ? ` la ${formatData(p.arhivatLa)}` : ''}${p.arhivatDe ? ` de ${p.arhivatDe}` : ''}`}
          description="Nu apare în liste și nu primește documente noi (doar returul materialelor rămase în șantier). Documentele lui rămân."
          action={poateEdita ? <Button size="small" icon={<RollbackOutlined />} loading={reactiveaza.isPending} onClick={() => void schimbaArhivarea(false)}>Reactivează</Button> : undefined} />
      )}

      <Card styles={{ body: { paddingTop: 0 } }}>
        <Tabs
          activeKey={tab}
          onChange={(t) => setParametri({ tab: t }, { replace: true })}
          items={[
            {
              key: 'general',
              label: 'Prezentare generală',
              children: (
                <Skeleton loading={proiect.isLoading} active>
                  {p && (
                    <Row gutter={[24, 24]}>
                      <Col xs={24} lg={16}>
                        <Descriptions
                          bordered
                          column={{ xs: 1, md: 2 }}
                          size="middle"
                          items={[
                            { label: 'Cod', children: p.cod },
                            { label: 'Stare', children: stariProiect[p.stare].eticheta },
                            { label: 'Tip', children: p.tip === 'Sediu' ? 'Sediu al firmei (cheltuieli de sediu)' : 'Lucrare', span: 2 },
                            { label: 'Denumire', children: p.denumire, span: 2 },
                            ...(p.tip === 'Sediu' ? [] : [
                              { label: 'Beneficiar', children: p.beneficiar },
                              { label: 'CUI beneficiar', children: p.cuiBeneficiar ?? '—' },
                            ]),
                            {
                              label: p.tip === 'Sediu' ? 'Adresa sediului' : 'Amplasament',
                              span: 2,
                              children: (
                                <>
                                  <EnvironmentOutlined /> {p.adresaSantier}
                                </>
                              ),
                            },
                            { label: 'Data începerii', children: formatData(p.dataInceput) },
                            { label: 'Termen contractual', children: formatData(p.termenContractual) },
                            { label: 'Creat la', children: formatData(p.creatLa) },
                          ]}
                        />
                      </Col>
                      <Col xs={24} lg={8}>
                        <Card size="small" title="Durata contractuală">
                          {scurs === null ? (
                            <Typography.Text type="secondary">Proiectul nu are încă date contractuale.</Typography.Text>
                          ) : (
                            <div style={{ textAlign: 'center', paddingBlock: 8 }}>
                              <Progress type="dashboard" percent={scurs} status={scurs >= 100 ? 'exception' : 'normal'} />
                              <div>timp scurs din durata contractuală</div>
                            </div>
                          )}
                        </Card>
                      </Col>
                    </Row>
                  )}
                </Skeleton>
              ),
            },
            {
              key: 'santiere',
              label: p?.tip === 'Sediu' ? 'Locații' : 'Șantiere',
              children: <SantiereProiect proiectId={proiectId} poateEdita={poateEdita} />,
            },
            {
              key: 'devize',
              label: `Devize${devize.data ? ` (${devize.data.length})` : ''}`,
              children: (
                <Table<VersiuneDeviz>
                  rowKey="id"
                  loading={devize.isLoading}
                  dataSource={devize.data}
                  pagination={false}
                  onRow={(v) => ({ onClick: () => navigheaza(`/proiecte/${proiectId}/devize/${v.id}`), style: { cursor: 'pointer' } })}
                  locale={{
                    emptyText: (
                      <Empty description="Proiectul nu are încă devize">
                        {poateImporta && (
                          <Button type="primary" icon={<UploadOutlined />} onClick={() => setImportDeschis(true)}>
                            Importă primul deviz
                          </Button>
                        )}
                      </Empty>
                    ),
                  }}
                  columns={[
                    { title: 'Tip', dataIndex: 'tip', width: 170, render: (t: VersiuneDeviz['tip']) => tipuriVersiune[t] },
                    { title: 'Nr.', dataIndex: 'numar', width: 70 },
                    { title: 'Denumire', dataIndex: 'denumire' },
                    { title: 'Sursă', dataIndex: 'sursa', width: 160 },
                    {
                      title: 'Stare',
                      dataIndex: 'stare',
                      width: 130,
                      render: (s: VersiuneDeviz['stare']) => <Tag color={stariVersiune[s].culoare}>{stariVersiune[s].eticheta}</Tag>,
                    },
                  ]}
                />
              ),
            },
            {
              key: 'contracte',
              label: 'Contracte',
              children: <ListaContracte proiectId={proiectId} />,
            },
            ...['Buget', 'Grafic', 'Situații', 'Documente'].map((t) => ({
              key: t.toLowerCase(),
              label: t,
              children: <Empty description={`Modulul „${t}” va fi disponibil într-o fază ulterioară.`} />,
            })),
          ]}
        />
      </Card>

      <ImportDeviz proiectId={proiectId} deschis={importDeschis} laInchidere={() => setImportDeschis(false)} />
      <EditareProiect proiect={p} deschis={editareDeschisa} laInchidere={() => setEditareDeschisa(false)} />
    </>
  )
}
