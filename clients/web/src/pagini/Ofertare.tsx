import { Card, Table, Tabs, Tag, Typography } from 'antd'
import { useNavigate, useSearchParams } from 'react-router'
import { usePermisiuni, useProiecte } from '../api/interogari'
import type { Proiect, StareProiect } from '../api/tipuri'
import { AntetPagina } from '../layout/AntetPagina'
import { stariProiect } from '../util/format'
import { ComparatieDevize, PreturiCatalog } from './devize/CatalogPreturi'
import { NomenclatorResurse } from './devize/NomenclatorResurse'

/**
 * Punct de intrare pentru ofertare: proiectele cu devizele lor, plus nomenclatorul de prețuri adunat din devizele
 * importate (resurse și articole) și compararea prețurilor între devize (D-54).
 */
export function Ofertare() {
  const { data, isLoading } = useProiecte()
  const navigheaza = useNavigate()
  const [parametri, setParametri] = useSearchParams()
  const permisiuni = usePermisiuni()
  // Prețurile din toate proiectele: cer și accesul la prețurile de cost.
  const vedePreturi = permisiuni.data?.includes('date.preturi_cost') ?? false
  const tab = parametri.get('tab') ?? 'proiecte'

  return (
    <>
      <AntetPagina
        titlu="Ofertare și devize"
        subtitlu="Proiectele cu devizele lor; prețurile resurselor și articolelor din toate devizele importate, comparate între devize."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Ofertare și devize' }]}
      />
      <Tabs
        activeKey={tab}
        onChange={(t) => setParametri(t === 'proiecte' ? {} : { tab: t }, { replace: true })}
        destroyOnHidden
        items={[
          {
            key: 'proiecte',
            label: 'Proiecte',
            children: (
              <Card styles={{ body: { padding: 0 } }}>
                <Table<Proiect>
                  rowKey="id"
                  loading={isLoading}
                  dataSource={data}
                  pagination={{ pageSize: 20, hideOnSinglePage: true }}
                  onRow={(p) => ({ onClick: () => navigheaza(`/proiecte/${p.id}?tab=devize`), style: { cursor: 'pointer' } })}
                  columns={[
                    { title: 'Cod', dataIndex: 'cod', width: 130, render: (v) => <Typography.Text strong>{v}</Typography.Text> },
                    { title: 'Denumire', dataIndex: 'denumire', ellipsis: true },
                    { title: 'Beneficiar', dataIndex: 'beneficiar', ellipsis: true },
                    {
                      title: 'Stare',
                      dataIndex: 'stare',
                      width: 170,
                      render: (s: StareProiect) => <Tag color={stariProiect[s].culoare}>{stariProiect[s].eticheta}</Tag>,
                    },
                  ]}
                />
              </Card>
            ),
          },
          { key: 'nomenclator', label: 'Nomenclator resurse', children: <NomenclatorResurse /> },
          ...(vedePreturi
            ? [
                { key: 'resurse', label: 'Prețuri resurse', children: <PreturiCatalog ce="resurse" /> },
                { key: 'articole', label: 'Prețuri articole', children: <PreturiCatalog ce="articole" /> },
                { key: 'comparatie', label: 'Comparație devize', children: <ComparatieDevize /> },
              ]
            : []),
        ]}
      />
    </>
  )
}
