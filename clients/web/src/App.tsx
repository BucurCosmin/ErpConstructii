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
import { DetaliiDeviz } from './pagini/devize/DetaliiDeviz'
import { DetaliiProiect } from './pagini/proiecte/DetaliiProiect'
import { ListaProiecte } from './pagini/proiecte/ListaProiecte'
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
  {
    element: <AplicatieLayout />,
    children: [
      { index: true, element: <TabloDeBord /> },
      { path: 'proiecte', element: <ListaProiecte /> },
      { path: 'proiecte/:proiectId', element: <DetaliiProiect /> },
      { path: 'proiecte/:proiectId/devize/:versiuneId', element: <DetaliiDeviz /> },
      { path: 'ofertare', element: <Ofertare /> },
      ...module.filter((m) => !m.implementat).map((m) => ({ path: m.cale.slice(1), element: <ModulInDezvoltare modul={m} /> })),
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
