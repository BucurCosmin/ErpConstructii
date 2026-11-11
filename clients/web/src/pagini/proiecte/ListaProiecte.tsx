import { DownloadOutlined, PlusOutlined, RollbackOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Flex, Input, Segmented, Table, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import { useProiecteArhivate, useReactiveazaProiect } from '../../api/proiecte'
import type { Proiect, StareProiect } from '../../api/tipuri'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData, stariProiect } from '../../util/format'
import { ProiectNou } from './ProiectNou'

type Filtru = 'toate' | 'active' | 'arhivate' | StareProiect

export function ListaProiecte() {
  const { message } = App.useApp()
  const active = useProiecte()
  const permisiuni = usePermisiuni()
  const navigheaza = useNavigate()
  const [parametri, setParametri] = useSearchParams()
  const [filtru, setFiltru] = useState<Filtru>('active')
  const [formularDeschis, setFormularDeschis] = useState(false)
  const cauta = parametri.get('cauta') ?? ''

  const poateCrea = permisiuni.data?.includes('proiecte.creare') ?? false
  const poateEdita = permisiuni.data?.includes('proiecte.editare') ?? false
  // D-39: arhivatele vin dintr-o listă separată (cea obișnuită le ascunde peste tot în aplicație).
  const arhivate = useProiecteArhivate(filtru === 'arhivate')
  const reactiveaza = useReactiveazaProiect()
  const { data, isLoading, error } = filtru === 'arhivate' ? arhivate : active

  const reactiveazaProiect = async (p: Proiect) => {
    try {
      await reactiveaza.mutateAsync(p.id)
      message.success(`Proiectul ${p.cod} a fost reactivat: apare din nou în liste.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const filtrate = useMemo(() => {
    const t = cauta.trim().toLowerCase()
    return (data ?? []).filter((p) => {
      if (filtru === 'active' && ['Inchis', 'Pierdut'].includes(p.stare)) return false
      if (filtru !== 'toate' && filtru !== 'active' && filtru !== 'arhivate' && p.stare !== filtru) return false
      if (!t) return true
      return [p.cod, p.denumire, p.beneficiar, p.adresaSantier, p.cuiBeneficiar ?? ''].some((c) => c.toLowerCase().includes(t))
    })
  }, [data, filtru, cauta])

  return (
    <>
      <AntetPagina
        titlu="Proiecte"
        subtitlu="Lucrările și sediile firmei: fiecare cost și venit se leagă de un proiect (cheltuielile de sediu, de sediul lor)."
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
              { value: 'arhivate', label: 'Arhivate' },
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
            {
              title: 'Denumire', dataIndex: 'denumire', ellipsis: true, sorter: (a, b) => a.denumire.localeCompare(b.denumire),
              render: (v: string, p) => (p.tip === 'Sediu' ? <>{v} <Tag color="purple">sediu</Tag></> : v),
            },
            {
              title: 'Beneficiar', dataIndex: 'beneficiar', ellipsis: true, sorter: (a, b) => a.beneficiar.localeCompare(b.beneficiar),
              render: (v: string, p) => (p.tip === 'Sediu' ? <Typography.Text type="secondary">sediul firmei</Typography.Text> : v),
            },
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
            ...(filtru === 'arhivate'
              ? [
                  {
                    title: 'Arhivat', key: 'arhivat', width: 190,
                    render: (_: unknown, p: Proiect) => <Typography.Text type="secondary">{formatData(p.arhivatLa)}{p.arhivatDe ? ` · ${p.arhivatDe}` : ''}</Typography.Text>,
                  },
                  {
                    title: '', key: 'reactivare', width: 140, fixed: 'right' as const,
                    render: (_: unknown, p: Proiect) => (
                      <Button size="small" icon={<RollbackOutlined />} disabled={!poateEdita} loading={reactiveaza.isPending}
                        onClick={(e) => { e.stopPropagation(); void reactiveazaProiect(p) }}>
                        Reactivează
                      </Button>
                    ),
                  },
                ]
              : []),
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
