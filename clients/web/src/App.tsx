import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App as AntApp, ConfigProvider, Result } from 'antd'
import roRO from 'antd/locale/ro_RO'
import dayjs from 'dayjs'
import 'dayjs/locale/ro'
import { Link, RouterProvider, createBrowserRouter } from 'react-router'
import { AplicatieLayout } from './layout/AplicatieLayout'
import { module } from './layout/module'
import { ModulInDezvoltare } from './pagini/ModulInDezvoltare'
import { Ofertare } from './pagini/Ofertare'
import { TabloDeBord } from './pagini/TabloDeBord'
import { Autentificare } from './pagini/autentificare/Autentificare'
import { Callback } from './pagini/autentificare/Callback'
import { ConfigurareMfa } from './pagini/autentificare/ConfigurareMfa'
import { SchimbareParola } from './pagini/autentificare/SchimbareParola'
import { Administrare } from './pagini/administrare/Administrare'
import { Facturi } from './pagini/facturi/Facturi'
import { DetaliiDeviz } from './pagini/devize/DetaliiDeviz'
import { DetaliiProiect } from './pagini/proiecte/DetaliiProiect'
import { ListaProiecte } from './pagini/proiecte/ListaProiecte'
import { Furnizori } from './pagini/furnizori/Furnizori'
import { Formulare } from './pagini/formulare/Formulare'
import { Aprovizionare } from './pagini/aprovizionare/Aprovizionare'
import { Stocuri } from './pagini/stocuri/Stocuri'
import { CentruImport } from './pagini/import/CentruImport'
import { tema } from './tema'

dayjs.locale('ro')

const clientInterogari = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
  },
})

const router = createBrowserRouter([
  // Paginile de autentificare: fără meniu și fără sesiune.
  { path: 'autentificare', element: <Autentificare /> },
  { path: 'autentificare/configurare-mfa', element: <ConfigurareMfa /> },
  { path: 'autentificare/callback', element: <Callback /> },
  { path: 'autentificare/schimbare-parola', element: <SchimbareParola /> },
  {
    element: <AplicatieLayout />,
    children: [
      { index: true, element: <TabloDeBord /> },
      { path: 'proiecte', element: <ListaProiecte /> },
      { path: 'proiecte/:proiectId', element: <DetaliiProiect /> },
      { path: 'proiecte/:proiectId/devize/:versiuneId', element: <DetaliiDeviz /> },
      { path: 'ofertare', element: <Ofertare /> },
      { path: 'furnizori', element: <Furnizori /> },
      { path: 'formulare', element: <Formulare /> },
      { path: 'aprovizionare', element: <Aprovizionare /> },
      { path: 'stocuri', element: <Stocuri /> },
      { path: 'import', element: <CentruImport /> },
      { path: 'facturi', element: <Facturi /> },
      { path: 'administrare/:tab?', element: <Administrare /> },
      ...module
        .filter((m) => !m.implementat && !['aprovizionare', 'formulare'].includes(m.cheie))
        .map((m) => ({ path: m.cale.slice(1), element: <ModulInDezvoltare modul={m} /> })),
      {
        path: '*',
        element: <Result status="404" title="Pagina nu există" extra={<Link to="/">Înapoi la tabloul de bord</Link>} />,
      },
    ],
  },
])

export function App() {
  return (
    <ConfigProvider locale={roRO} theme={tema}>
      <AntApp>
        <QueryClientProvider client={clientInterogari}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </AntApp>
    </ConfigProvider>
  )
}
