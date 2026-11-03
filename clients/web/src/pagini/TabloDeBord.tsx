import { CalendarOutlined, CheckCircleOutlined, FileSearchOutlined, ProjectOutlined } from '@ant-design/icons'
import { Alert, Card, Col, Empty, Flex, List, Progress, Row, Skeleton, Statistic, Table, Tag, Typography, theme } from 'antd'
import dayjs from 'dayjs'
import { useNavigate } from 'react-router'
import { useProfil, useProiecte } from '../api/interogari'
import type { Proiect, StareProiect } from '../api/tipuri'
import { AntetPagina } from '../layout/AntetPagina'
import { formatData, stariProiect, timpScurs } from '../util/format'

export function TabloDeBord() {
  const { data: proiecte, isLoading, error } = useProiecte()
  const profil = useProfil()
  const navigheaza = useNavigate()
  const { token } = theme.useToken()

  const toate = proiecte ?? []
  const peStare = (s: StareProiect) => toate.filter((p) => p.stare === s).length
  const inExecutie = toate.filter((p) => p.stare === 'InExecutie')
  const termene = toate
    .filter((p) => p.termenContractual && ['Contractat', 'InExecutie'].includes(p.stare))
    .sort((a, b) => a.termenContractual!.localeCompare(b.termenContractual!))
    .slice(0, 5)

  const salut = dayjs().hour() < 12 ? 'Bună dimineața' : dayjs().hour() < 18 ? 'Bună ziua' : 'Bună seara'
  const prenume = profil.data?.nume.split(' ')[0]

  return (
    <>
      <AntetPagina
        titlu={prenume ? `${salut}, ${prenume}` : 'Tablou de bord'}
        subtitlu={`Situația portofoliului la ${dayjs().format('DD MMMM YYYY')}`}
      />

      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 16 }} />}

      <Row gutter={[16, 16]}>
        {[
          { titlu: 'Proiecte active', valoare: toate.filter((p) => !['Inchis', 'Pierdut'].includes(p.stare)).length, icon: <ProjectOutlined />, culoare: token.colorPrimary },
          { titlu: 'În execuție', valoare: peStare('InExecutie'), icon: <CheckCircleOutlined />, culoare: token.colorSuccess },
          { titlu: 'În ofertare', valoare: peStare('Ofertare'), icon: <FileSearchOutlined />, culoare: token.colorInfo },
          {
            titlu: 'Termene în 90 de zile',
            valoare: toate.filter((p) => p.termenContractual && dayjs(p.termenContractual).diff(dayjs(), 'day') <= 90 && dayjs(p.termenContractual).isAfter(dayjs())).length,
            icon: <CalendarOutlined />,
            culoare: token.colorWarning,
          },
        ].map((k) => (
          <Col xs={24} sm={12} xl={6} key={k.titlu}>
            <Card>
              <Skeleton loading={isLoading} active paragraph={false}>
                <Flex justify="space-between" align="center">
                  <Statistic title={k.titlu} value={k.valoare} />
                  <div
                    style={{
                      width: 48,
                      height: 48,
                      borderRadius: 12,
                      display: 'grid',
                      placeItems: 'center',
                      fontSize: 22,
                      color: k.culoare,
                      background: `color-mix(in srgb, ${k.culoare} 12%, transparent)`,
                    }}
                  >
                    {k.icon}
                  </div>
                </Flex>
              </Skeleton>
            </Card>
          </Col>
        ))}

        <Col xs={24} xl={16}>
          <Card title="Șantiere în execuție" extra={<a onClick={() => navigheaza('/proiecte')}>Toate proiectele</a>}>
            <Table<Proiect>
              rowKey="id"
              size="middle"
              loading={isLoading}
              dataSource={inExecutie}
              pagination={false}
              onRow={(p) => ({ onClick: () => navigheaza(`/proiecte/${p.id}`), style: { cursor: 'pointer' } })}
              locale={{ emptyText: <Empty description="Niciun șantier în execuție" /> }}
              columns={[
                {
                  title: 'Proiect',
                  render: (_, p) => (
                    <>
                      <Typography.Text strong>{p.cod}</Typography.Text>
                      <div style={{ color: token.colorTextSecondary }}>{p.denumire}</div>
                    </>
                  ),
                },
                { title: 'Beneficiar', dataIndex: 'beneficiar', responsive: ['lg'] },
                { title: 'Termen', dataIndex: 'termenContractual', render: formatData, width: 110 },
                {
                  title: 'Timp scurs',
                  width: 180,
                  render: (_, p) => {
                    const v = timpScurs(p.dataInceput, p.termenContractual)
                    return v === null ? '—' : <Progress percent={v} size="small" status={v >= 100 ? 'exception' : 'normal'} />
                  },
                },
              ]}
            />
          </Card>
        </Col>

        <Col xs={24} xl={8}>
          <Flex vertical gap={16}>
            <Card title="Următoarele termene contractuale">
              <List
                loading={isLoading}
                dataSource={termene}
                locale={{ emptyText: 'Niciun termen' }}
                renderItem={(p) => {
                  const zile = dayjs(p.termenContractual).diff(dayjs(), 'day')
                  return (
                    <List.Item style={{ cursor: 'pointer' }} onClick={() => navigheaza(`/proiecte/${p.id}`)}>
                      <List.Item.Meta title={p.cod} description={p.denumire} />
                      <div style={{ textAlign: 'right' }}>
                        <div>{formatData(p.termenContractual)}</div>
                        <Tag color={zile < 0 ? 'red' : zile <= 90 ? 'orange' : 'default'} style={{ marginInlineEnd: 0 }}>
                          {zile < 0 ? `depășit cu ${-zile} zile` : `${zile} zile`}
                        </Tag>
                      </div>
                    </List.Item>
                  )
                }}
              />
            </Card>

            <Card title="Portofoliu pe stări">
              <Flex vertical gap={10}>
                {(Object.keys(stariProiect) as StareProiect[])
                  .map((s) => ({ s, n: peStare(s) }))
                  .filter((x) => x.n > 0)
                  .map(({ s, n }) => (
                    <div key={s}>
                      <Flex justify="space-between">
                        <Tag color={stariProiect[s].culoare}>{stariProiect[s].eticheta}</Tag>
                        <Typography.Text strong>{n}</Typography.Text>
                      </Flex>
                      <Progress percent={toate.length ? Math.round((n / toate.length) * 100) : 0} showInfo={false} size="small" />
                    </div>
                  ))}
              </Flex>
            </Card>
          </Flex>
        </Col>
      </Row>
    </>
  )
}
