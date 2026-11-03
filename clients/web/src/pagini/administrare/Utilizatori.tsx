import { LockOutlined, SafetyOutlined, SearchOutlined, UserAddOutlined } from '@ant-design/icons'
import { Alert, Avatar, Button, Flex, Input, Segmented, Space, Table, Tag, Tooltip, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useUtilizatori } from '../../api/administrare'
import { usePermisiuni } from '../../api/interogari'
import type { UtilizatorLista } from '../../api/tipuri'
import { tipuriUtilizator } from '../../util/format'
import { FisaUtilizator } from './FisaUtilizator'
import { UtilizatorNou } from './UtilizatorNou'

type Filtru = 'cu_acces' | 'fara_acces' | 'toti'

export function Utilizatori() {
  const { data, isLoading, error } = useUtilizatori()
  const permisiuni = usePermisiuni()
  const [cauta, setCauta] = useState('')
  const [filtru, setFiltru] = useState<Filtru>('cu_acces')
  const [selectat, setSelectat] = useState<string | null>(null)
  const [nouDeschis, setNouDeschis] = useState(false)
  const poateCrea = permisiuni.data?.includes('administrare.creare') ?? false

  const filtrati = useMemo(() => {
    const t = cauta.trim().toLowerCase()
    return (data ?? []).filter(
      (u) =>
        (filtru === 'toti' || (filtru === 'cu_acces') === u.areAcces) &&
        (!t || u.nume.toLowerCase().includes(t) || u.email.includes(t) || u.roluri.some((r) => r.toLowerCase().includes(t))),
    )
  }, [data, cauta, filtru])

  const nr = (f: Filtru) => (data ?? []).filter((u) => f === 'toti' || (f === 'cu_acces') === u.areAcces).length

  return (
    <>
      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 16 }} />}
      <Flex gap={12} wrap justify="space-between" style={{ marginBlock: 8 }}>
        <Space wrap>
          <Segmented<Filtru>
            value={filtru}
            onChange={setFiltru}
            options={[
              { value: 'cu_acces', label: `Cu acces (${nr('cu_acces')})` },
              { value: 'fara_acces', label: `Fără acces (${nr('fara_acces')})` },
              { value: 'toti', label: `Toți (${nr('toti')})` },
            ]}
          />
          <Input
            prefix={<SearchOutlined />}
            placeholder="Nume, email sau rol…"
            allowClear
            style={{ width: 280 }}
            value={cauta}
            onChange={(e) => setCauta(e.target.value)}
          />
        </Space>
        <Button type="primary" icon={<UserAddOutlined />} disabled={!poateCrea} onClick={() => setNouDeschis(true)}>
          Utilizator nou
        </Button>
      </Flex>

      <Table<UtilizatorLista>
        rowKey="id"
        loading={isLoading}
        dataSource={filtrati}
        pagination={{ pageSize: 25, hideOnSinglePage: true }}
        onRow={(u) => ({ onClick: () => setSelectat(u.id), style: { cursor: 'pointer' } })}
        columns={[
          {
            title: 'Utilizator',
            sorter: (a, b) => a.nume.localeCompare(b.nume),
            render: (_, u) => (
              <Flex gap={12} align="center">
                <Avatar style={{ background: u.areAcces ? '#1f5fbf' : '#bfbfbf', flex: 'none' }}>
                  {u.nume
                    .split(' ')
                    .map((p) => p[0])
                    .join('')
                    .slice(0, 2)}
                </Avatar>
                <div style={{ minWidth: 0 }}>
                  <Typography.Text strong>{u.nume}</Typography.Text>
                  <div>
                    <Typography.Text type="secondary">{u.email}</Typography.Text>
                  </div>
                </div>
              </Flex>
            ),
          },
          { title: 'Tip', dataIndex: 'tip', width: 190, render: (t: UtilizatorLista['tip']) => tipuriUtilizator[t].eticheta, responsive: ['lg'] },
          {
            title: 'Roluri în firmă',
            dataIndex: 'roluri',
            render: (r: string[]) =>
              r.length ? (
                <Flex gap={4} wrap>
                  {r.map((x) => (
                    <Tag key={x} style={{ marginInlineEnd: 0 }}>
                      {x}
                    </Tag>
                  ))}
                </Flex>
              ) : (
                <Typography.Text type="secondary">—</Typography.Text>
              ),
          },
          {
            title: 'Stare',
            width: 230,
            render: (_, u) => (
              <Flex gap={4} wrap>
                {!u.activ ? (
                  <Tag color="red">Dezactivat</Tag>
                ) : u.areAcces ? (
                  <Tag color="green">Activ</Tag>
                ) : (
                  <Tag>Fără acces</Tag>
                )}
                {u.blocat && (
                  <Tooltip title="Blocat temporar după parole greșite">
                    <Tag icon={<LockOutlined />} color="orange">
                      Blocat
                    </Tag>
                  </Tooltip>
                )}
                {u.trebuieSchimbataParola && (
                  <Tooltip title="Nu s-a autentificat încă după primirea parolei temporare">
                    <Tag color="gold">Parolă temporară</Tag>
                  </Tooltip>
                )}
                {u.mfaActivat && (
                  <Tooltip title="Verificare în doi pași activă">
                    <Tag icon={<SafetyOutlined />} color="blue">
                      MFA
                    </Tag>
                  </Tooltip>
                )}
              </Flex>
            ),
          },
        ]}
      />

      <FisaUtilizator id={selectat} laInchidere={() => setSelectat(null)} />
      <UtilizatorNou deschis={nouDeschis} laInchidere={() => setNouDeschis(false)} laCreare={(id) => setSelectat(id)} />
    </>
  )
}
