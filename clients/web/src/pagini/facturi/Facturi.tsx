import { Card, Tabs } from 'antd'
import { useNavigate, useSearchParams } from 'react-router'
import { usePermisiuni } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'
import { ImportFactura } from './ImportFactura'
import { ListaFacturiPrimite } from './ListaFacturiPrimite'

/** Facturile primite: lista (pe proiectele accesibile), rapoartele și importul din e-Factura / PDF. */
export function Facturi() {
  const [parametri, setParametri] = useSearchParams()
  const navigheaza = useNavigate()
  const tab = parametri.get('tab') ?? 'primite'
  const permisiuni = usePermisiuni()
  const poateCrea = permisiuni.data?.includes('financiar.creare') ?? false
  const poateAnula = permisiuni.data?.includes('financiar.anulare') ?? false

  return (
    <>
      <AntetPagina
        titlu="Facturi primite"
        subtitlu="Facturile furnizorilor, înregistrate pe proiecte: din e-Factura (XML) sau din PDF / scanare."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Facturi' }]}
      />
      <Card styles={{ body: { paddingTop: 4 } }}>
        <Tabs
          activeKey={tab}
          onChange={(t) => setParametri({ tab: t }, { replace: true })}
          items={[
            {
              key: 'primite',
              label: 'Facturi primite',
              children: <ListaFacturiPrimite poateCrea={poateCrea} poateAnula={poateAnula} onImport={() => navigheaza('/facturi?tab=import')} />,
            },
            { key: 'import', label: 'Import factură', children: <ImportFactura poateCrea={poateCrea} /> },
          ]}
        />
      </Card>
    </>
  )
}
