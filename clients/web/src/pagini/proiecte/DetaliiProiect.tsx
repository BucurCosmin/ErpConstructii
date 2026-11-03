import { EditOutlined, EnvironmentOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, Descriptions, Empty, Progress, Result, Row, Skeleton, Table, Tabs, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { EroareApi } from '../../api/client'
import { useDevize, usePermisiuni, useProiect } from '../../api/interogari'
import type { VersiuneDeviz } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData, stariProiect, stariVersiune, timpScurs, tipuriVersiune } from '../../util/format'
import { ImportDeviz } from '../devize/ImportDeviz'

export function DetaliiProiect() {
  const { proiectId = '' } = useParams()
  const [parametri, setParametri] = useSearchParams()
  const proiect = useProiect(proiectId)
  const devize = useDevize(proiectId)
  const permisiuni = usePermisiuni()
  const navigheaza = useNavigate()
  const [importDeschis, setImportDeschis] = useState(false)
  const tab = parametri.get('tab') ?? 'general'

  if (proiect.error instanceof EroareApi && proiect.error.status === 404) {
    return <Result status="404" title="Proiect inexistent" extra={<Link to="/proiecte">Înapoi la proiecte</Link>} />
  }

  const p = proiect.data
  const scurs = p ? timpScurs(p.dataInceput, p.termenContractual) : null
  const poateImporta = permisiuni.data?.includes('ofertare.creare') ?? false

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
            <Button icon={<EditOutlined />} disabled>
              Editează
            </Button>
            <Button type="primary" icon={<UploadOutlined />} disabled={!poateImporta} onClick={() => setImportDeschis(true)}>
              Importă deviz
            </Button>
          </>
        }
      />

      {proiect.error && !(proiect.error instanceof EroareApi && proiect.error.status === 404) && (
        <Alert type="error" showIcon title={(proiect.error as Error).message} style={{ marginBottom: 16 }} />
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
                            { label: 'Denumire', children: p.denumire, span: 2 },
                            { label: 'Beneficiar', children: p.beneficiar },
                            { label: 'CUI beneficiar', children: p.cuiBeneficiar ?? '—' },
                            {
                              label: 'Amplasament',
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
            ...['Contracte', 'Buget', 'Grafic', 'Situații', 'Documente'].map((t) => ({
              key: t.toLowerCase(),
              label: t,
              children: <Empty description={`Modulul „${t}” va fi disponibil într-o fază ulterioară.`} />,
            })),
          ]}
        />
      </Card>

      <ImportDeviz proiectId={proiectId} deschis={importDeschis} laInchidere={() => setImportDeschis(false)} />
    </>
  )
}
