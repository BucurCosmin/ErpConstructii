import {
  BellOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  QuestionCircleOutlined,
  SearchOutlined,
  SwapOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Avatar, Badge, Button, Dropdown, Input, Layout, Menu, Select, Space, Tooltip, Typography, theme } from 'antd'
import type { MenuProps } from 'antd'
import { useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { sesiuneCurenta } from '../api/client'
import { usePermisiuni, useProfil } from '../api/interogari'
import { grupuri, module } from './module'

const { Header, Sider, Content } = Layout

export function AplicatieLayout() {
  const [restrans, setRestrans] = useState(false)
  const navigheaza = useNavigate()
  const locatie = useLocation()
  const { token } = theme.useToken()
  const profil = useProfil()
  const permisiuni = usePermisiuni()
  const sesiune = sesiuneCurenta()

  const elementeMeniu = useMemo<MenuProps['items']>(() => {
    const acordate = new Set(permisiuni.data ?? [])
    const vizibile = module.filter((m) => !m.permisiune || acordate.has(m.permisiune))
    const elemente: NonNullable<MenuProps['items']> = []
    for (const [grup, titlu] of Object.entries(grupuri)) {
      const dinGrup = vizibile
        .filter((m) => m.grup === grup)
        .map((m) => ({ key: m.cale, icon: m.icon, label: m.eticheta }))
      if (dinGrup.length === 0) continue
      if (titlu) elemente.push({ type: 'group', key: grup, label: restrans ? null : titlu, children: dinGrup })
      else elemente.push(...dinGrup)
    }
    return elemente
  }, [permisiuni.data, restrans])

  // Elementul activ: cel mai lung prefix de rută (ex. /proiecte/123 → /proiecte).
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

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        collapsible
        collapsed={restrans}
        trigger={null}
        width={248}
        collapsedWidth={72}
        style={{ position: 'sticky', top: 0, height: '100vh', overflow: 'auto' }}
      >
        <div
          style={{
            height: 64,
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            padding: restrans ? '0 20px' : '0 20px',
            borderBottom: '1px solid rgba(255,255,255,0.08)',
          }}
        >
          <img src="/favicon.svg" alt="" width={32} height={32} />
          {!restrans && (
            <div style={{ lineHeight: 1.15 }}>
              <div style={{ color: '#fff', fontWeight: 700, fontSize: 16, letterSpacing: 0.2 }}>ERP Construcții</div>
              <div style={{ color: 'rgba(255,255,255,0.55)', fontSize: 12 }}>Management proiecte</div>
            </div>
          )}
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[selectat]}
          items={elementeMeniu}
          onClick={(e) => navigheaza(e.key)}
          style={{ borderInlineEnd: 0, paddingBlock: 8 }}
        />
      </Sider>

      <Layout>
        <Header
          style={{
            position: 'sticky',
            top: 0,
            zIndex: 10,
            display: 'flex',
            alignItems: 'center',
            gap: 16,
            paddingInline: 16,
            background: token.colorBgContainer,
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
          }}
        >
          <Button
            type="text"
            icon={restrans ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setRestrans(!restrans)}
            aria-label="Restrânge meniul"
          />

          <Select
            value={sesiune?.firmaId}
            style={{ minWidth: 220 }}
            suffixIcon={<SwapOutlined />}
            options={sesiune ? [{ value: sesiune.firmaId, label: sesiune.firmaNume }] : []}
            aria-label="Firma activă"
          />

          <Input
            prefix={<SearchOutlined style={{ color: token.colorTextTertiary }} />}
            placeholder="Caută proiecte, devize, documente…"
            style={{ maxWidth: 420 }}
            allowClear
            onPressEnter={(e) => {
              const text = (e.target as HTMLInputElement).value.trim()
              if (text) navigheaza(`/proiecte?cauta=${encodeURIComponent(text)}`)
            }}
          />

          <div style={{ flex: 1 }} />

          <Space size={4}>
            <Tooltip title="Ajutor">
              <Button type="text" icon={<QuestionCircleOutlined />} />
            </Tooltip>
            <Tooltip title="Notificări">
              <Badge count={0} size="small">
                <Button type="text" icon={<BellOutlined />} />
              </Badge>
            </Tooltip>
            <Dropdown
              menu={{
                items: [
                  { key: 'profil', icon: <UserOutlined />, label: 'Profilul meu' },
                  { type: 'divider' },
                  { key: 'iesire', icon: <LogoutOutlined />, label: 'Ieșire', disabled: true },
                ],
              }}
              placement="bottomRight"
            >
              <Space style={{ cursor: 'pointer', paddingInline: 8 }}>
                <Avatar style={{ background: token.colorPrimary }}>{initiale}</Avatar>
                <div style={{ lineHeight: 1.2 }}>
                  <Typography.Text strong style={{ display: 'block' }}>
                    {profil.data?.nume ?? '…'}
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {profil.data?.email}
                  </Typography.Text>
                </div>
              </Space>
            </Dropdown>
          </Space>
        </Header>

        <Content style={{ padding: 24 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
