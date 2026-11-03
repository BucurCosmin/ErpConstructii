import { ApartmentOutlined, BankOutlined, SafetyCertificateOutlined, TeamOutlined } from '@ant-design/icons'
import { Card, Tabs } from 'antd'
import { useNavigate, useParams } from 'react-router'
import { useProfil } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'
import { Firma } from './Firma'
import { Firme } from './Firme'
import { Roluri } from './Roluri'
import { Utilizatori } from './Utilizatori'

/** Administrarea firmei active: utilizatori, roluri, datele firmei; firmele (doar administratorul de sistem). */
export function Administrare() {
  const { tab = 'utilizatori' } = useParams()
  const navigheaza = useNavigate()
  const profil = useProfil()
  const firmaActiva = profil.data?.firme.find((f) => f.id === profil.data?.firmaActiva)
  const esteAdminSistem = profil.data?.tip === 'AdministratorSistem'

  const taburi = [
    { key: 'utilizatori', label: 'Utilizatori', icon: <TeamOutlined />, children: <Utilizatori /> },
    { key: 'roluri', label: 'Roluri și permisiuni', icon: <SafetyCertificateOutlined />, children: <Roluri /> },
    { key: 'firma', label: 'Datele firmei', icon: <BankOutlined />, children: <Firma /> },
    ...(esteAdminSistem ? [{ key: 'firme', label: 'Toate firmele', icon: <ApartmentOutlined />, children: <Firme /> }] : []),
  ]

  return (
    <>
      <AntetPagina
        titlu="Administrare"
        subtitlu={firmaActiva ? `Utilizatorii, rolurile și setările firmei ${firmaActiva.denumire}.` : undefined}
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Administrare' }]}
      />
      <Card styles={{ body: { paddingTop: 4 } }}>
        <Tabs activeKey={tab} onChange={(t) => navigheaza(`/administrare/${t}`)} items={taburi} destroyOnHidden />
      </Card>
    </>
  )
}
