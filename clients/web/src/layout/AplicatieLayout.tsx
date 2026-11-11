import {
  BellOutlined,
  DownOutlined,
  LogoutOutlined,
  QuestionCircleOutlined,
  SafetyOutlined,
  SwapOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { App, Avatar, Badge, Button, Dropdown, Layout, Menu, Select, Space, Tooltip, Typography, theme } from 'antd'
import type { MenuProps } from 'antd'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { iesire, schimbaFirma } from '../api/autentificare'
import { usePermisiuni, useProfil } from '../api/interogari'
import { grupuri, module } from './module'

const { Header, Content } = Layout

/**
 * Cadrul aplicației: o singură bară sus (logo, meniul pe grupuri, firma activă, contul), iar pagina ocupă
 * toată lățimea. Grupurile (Proiect, Execuție, Financiar, Sistem) se deschid ca meniuri derulante; modulele principale
 * stau direct pe bară. Toate secțiunile sunt mereu vizibile.
 */
export function AplicatieLayout() {
  const navigheaza = useNavigate()
  const locatie = useLocation()
  const { token } = theme.useToken()
  const profil = useProfil()
  const permisiuni = usePermisiuni()
  const clientInterogari = useQueryClient()
  const { message } = App.useApp()
  const [schimbaFirmaInCurs, setSchimbaFirmaInCurs] = useState(false)

  // Firma activă e în token: serverul emite token-uri noi, apoi toate datele se reîncarcă.
  const laSchimbareaFirmei = async (firmaId: string) => {
    setSchimbaFirmaInCurs(true)
    try {
      await schimbaFirma(firmaId)
      clientInterogari.clear()
      navigheaza('/')
      const firma = profil.data?.firme.find((f) => f.id === firmaId)
      message.success(`Lucrezi acum în ${firma?.denumire ?? 'firma selectată'}.`)
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSchimbaFirmaInCurs(false)
    }
  }

  const elementeMeniu = useMemo<MenuProps['items']>(() => {
    const acordate = new Set(permisiuni.data ?? [])
    const vizibile = module.filter((m) => !m.permisiune || [m.permisiune].flat().some((p) => acordate.has(p)))
    const elemente: NonNullable<MenuProps['items']> = []
    for (const [grup, titlu] of Object.entries(grupuri)) {
      const dinGrup = vizibile
        .filter((m) => m.grup === grup)
        .map((m) => ({ key: m.cale, icon: m.icon, label: m.eticheta }))
      if (dinGrup.length === 0) continue
      if (titlu) {
        elemente.push({
          key: `grup-${grup}`,
          label: (
            <span>
              {titlu} <DownOutlined style={{ fontSize: 10, marginInlineStart: 2 }} />
            </span>
          ),
          children: dinGrup,
        })
      } else {
        elemente.push(...dinGrup)
      }
    }
    return elemente
  }, [permisiuni.data])

  // Elementul activ: cel mai lung prefix de rută (ex. /proiecte/123 → /proiecte); grupul lui apare și el activ.
  const selectat =
    module
      .map((m) => m.cale)
      .filter((c) => (c === '/' ? locatie.pathname === '/' : locatie.pathname.startsWith(c)))
      .sort((a, b) => b.length - a.length)[0] ?? '/'

  const initiale = (profil.data?.nume ?? '?')
    .split(' ')
    .map((p) => p[0])
    .join('')
    .slice(0, 2)

  const textDeschis = 'rgba(255,255,255,0.85)'

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Header
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 10,
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          height: 56,
          lineHeight: '56px',
          paddingInline: 16,
        }}
      >
        <div
          role="link"
          tabIndex={0}
          onClick={() => navigheaza('/')}
          onKeyDown={(e) => e.key === 'Enter' && navigheaza('/')}
          style={{ display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', flexShrink: 0 }}
        >
          <img src="/favicon.svg" alt="" width={28} height={28} />
          <span style={{ color: '#fff', fontWeight: 700, fontSize: 15, letterSpacing: 0.2, whiteSpace: 'nowrap' }}>ERP Construcții</span>
        </div>

        <Menu
          theme="dark"
          mode="horizontal"
          selectedKeys={[selectat]}
          items={elementeMeniu}
          onClick={(e) => navigheaza(e.key)}
          // Toate secțiunile rămân vizibile pe bară (fără „…”); încap de la ~1280 px lățime.
          disabledOverflow
          style={{ flex: 1, minWidth: 0, borderBottom: 0, lineHeight: '56px', flexWrap: 'nowrap' }}
        />

        <Select
          value={profil.data?.firmaActiva ?? undefined}
          style={{ width: 220, flexShrink: 0 }}
          suffixIcon={<SwapOutlined />}
          loading={profil.isLoading || schimbaFirmaInCurs}
          disabled={schimbaFirmaInCurs}
          options={profil.data?.firme.map((f) => ({ value: f.id, label: f.denumire }))}
          onChange={laSchimbareaFirmei}
          aria-label="Firma activă"
          popupMatchSelectWidth={false}
        />

        <Space size={2} style={{ flexShrink: 0 }}>
          <Tooltip title="Ajutor">
            <Button type="text" icon={<QuestionCircleOutlined style={{ color: textDeschis }} />} />
          </Tooltip>
          <Tooltip title="Notificări">
            <Badge count={0} size="small">
              <Button type="text" icon={<BellOutlined style={{ color: textDeschis }} />} />
            </Badge>
          </Tooltip>
          <Dropdown
            menu={{
              items: [
                {
                  key: 'cine',
                  label: (
                    <div style={{ lineHeight: 1.3 }}>
                      <Typography.Text strong style={{ display: 'block' }}>{profil.data?.nume ?? '…'}</Typography.Text>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{profil.data?.email}</Typography.Text>
                    </div>
                  ),
                  disabled: true,
                },
                { type: 'divider' },
                { key: 'profil', icon: <UserOutlined />, label: 'Profilul meu', disabled: true },
                {
                  key: 'mfa',
                  icon: <SafetyOutlined />,
                  label: profil.data?.mfaActivat ? 'Verificare în doi pași: activă' : 'Verificare în doi pași: inactivă',
                  disabled: true,
                },
                { type: 'divider' },
                { key: 'iesire', icon: <LogoutOutlined />, label: 'Ieșire', danger: true },
              ],
              onClick: ({ key }) => {
                if (key === 'iesire') void iesire()
              },
            }}
            placement="bottomRight"
          >
            <Tooltip title={profil.data?.nume} placement="left">
              <Avatar style={{ background: token.colorPrimary, cursor: 'pointer', marginInlineStart: 4 }}>{initiale}</Avatar>
            </Tooltip>
          </Dropdown>
        </Space>
      </Header>

      <Content style={{ padding: '16px 20px' }}>
        <Outlet />
      </Content>
    </Layout>
  )
}
