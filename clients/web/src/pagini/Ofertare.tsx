import { Card, Table, Tag, Typography } from 'antd'
import { useNavigate } from 'react-router'
import { useProiecte } from '../api/interogari'
import type { Proiect, StareProiect } from '../api/tipuri'
import { AntetPagina } from '../layout/AntetPagina'
import { stariProiect } from '../util/format'

/** Punct de intrare pentru ofertare: proiectele cu devizele lor. */
export function Ofertare() {
  const { data, isLoading } = useProiecte()
  const navigheaza = useNavigate()

  return (
    <>
      <AntetPagina
        titlu="Ofertare și devize"
        subtitlu="Alege proiectul pentru a vedea versiunile de deviz (ofertă, contract, buget, acte adiționale) sau pentru a importa unul nou."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Ofertare și devize' }]}
      />
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
    </>
  )
}
