import { DownloadOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Flex, Input, Segmented, Table, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import type { Proiect, StareProiect } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData, stariProiect } from '../../util/format'
import { ProiectNou } from './ProiectNou'

type Filtru = 'toate' | 'active' | StareProiect

export function ListaProiecte() {
  const { data, isLoading, error } = useProiecte()
  const permisiuni = usePermisiuni()
  const navigheaza = useNavigate()
  const [parametri, setParametri] = useSearchParams()
  const [filtru, setFiltru] = useState<Filtru>('active')
  const [formularDeschis, setFormularDeschis] = useState(false)
  const cauta = parametri.get('cauta') ?? ''

  const poateCrea = permisiuni.data?.includes('proiecte.creare') ?? false

  const filtrate = useMemo(() => {
    const t = cauta.trim().toLowerCase()
    return (data ?? []).filter((p) => {
      if (filtru === 'active' && ['Inchis', 'Pierdut'].includes(p.stare)) return false
      if (filtru !== 'toate' && filtru !== 'active' && p.stare !== filtru) return false
      if (!t) return true
      return [p.cod, p.denumire, p.beneficiar, p.adresaSantier, p.cuiBeneficiar ?? ''].some((c) => c.toLowerCase().includes(t))
    })
  }, [data, filtru, cauta])

  return (
    <>
      <AntetPagina
        titlu="Proiecte"
        subtitlu="Șantierele firmei: fiecare cost și venit se leagă de un proiect."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Proiecte' }]}
        actiuni={
          <>
            <Button icon={<DownloadOutlined />} disabled>
              Export
            </Button>
            <Button type="primary" icon={<PlusOutlined />} disabled={!poateCrea} onClick={() => setFormularDeschis(true)}>
              Proiect nou
            </Button>
          </>
        }
      />

      {error && <Alert type="error" showIcon title={(error as Error).message} style={{ marginBottom: 16 }} />}

      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap justify="space-between" style={{ padding: 16 }}>
          <Segmented<Filtru>
            value={filtru}
            onChange={setFiltru}
            options={[
              { value: 'active', label: 'Active' },
              { value: 'Ofertare', label: 'Ofertare' },
              { value: 'Contractat', label: 'Contractate' },
              { value: 'InExecutie', label: 'În execuție' },
              { value: 'toate', label: 'Toate' },
            ]}
          />
          <Input
            prefix={<SearchOutlined />}
            placeholder="Cod, denumire, beneficiar, CUI…"
            allowClear
            style={{ width: 320 }}
            value={cauta}
            onChange={(e) => setParametri(e.target.value ? { cauta: e.target.value } : {}, { replace: true })}
          />
        </Flex>

        <Table<Proiect>
          rowKey="id"
          loading={isLoading}
          dataSource={filtrate}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => `${n} proiecte` }}
          onRow={(p) => ({ onClick: () => navigheaza(`/proiecte/${p.id}`), style: { cursor: 'pointer' } })}
          scroll={{ x: 1000 }}
          columns={[
            {
              title: 'Cod',
              dataIndex: 'cod',
              width: 130,
              sorter: (a, b) => a.cod.localeCompare(b.cod),
              defaultSortOrder: 'descend',
              render: (v) => <Typography.Text strong>{v}</Typography.Text>,
            },
            { title: 'Denumire', dataIndex: 'denumire', ellipsis: true, sorter: (a, b) => a.denumire.localeCompare(b.denumire) },
            { title: 'Beneficiar', dataIndex: 'beneficiar', ellipsis: true, sorter: (a, b) => a.beneficiar.localeCompare(b.beneficiar) },
            { title: 'Amplasament', dataIndex: 'adresaSantier', ellipsis: true, responsive: ['xl'] },
            {
              title: 'Stare',
              dataIndex: 'stare',
              width: 170,
              render: (s: StareProiect) => <Tag color={stariProiect[s].culoare}>{stariProiect[s].eticheta}</Tag>,
            },
            { title: 'Început', dataIndex: 'dataInceput', width: 110, render: formatData },
            {
              title: 'Termen',
              dataIndex: 'termenContractual',
              width: 110,
              render: formatData,
              sorter: (a, b) => (a.termenContractual ?? '').localeCompare(b.termenContractual ?? ''),
            },
          ]}
        />
      </Card>

      <ProiectNou
        deschis={formularDeschis}
        laInchidere={() => setFormularDeschis(false)}
        laCreare={(id) => {
          setFormularDeschis(false)
          navigheaza(`/proiecte/${id}`)
        }}
      />
    </>
  )
}
