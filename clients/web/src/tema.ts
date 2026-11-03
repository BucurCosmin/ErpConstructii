import type { ThemeConfig } from 'antd'

/** Tema vizuală: bleumarin sobru pentru navigare, accent albastru, densitate potrivită pentru tabele mari. */
export const tema: ThemeConfig = {
  token: {
    colorPrimary: '#1f5fbf',
    colorInfo: '#1f5fbf',
    colorSuccess: '#2e8b57',
    colorWarning: '#d98a00',
    colorError: '#c8372d',
    borderRadius: 8,
    fontFamily: "Inter, 'Segoe UI', system-ui, -apple-system, Roboto, 'Helvetica Neue', Arial, sans-serif",
    colorBgLayout: '#f3f5f8',
  },
  components: {
    Layout: {
      siderBg: '#14294a',
      headerHeight: 64,
    },
    Menu: {
      darkItemBg: '#14294a',
      darkSubMenuItemBg: '#10223e',
      darkItemSelectedBg: '#1f5fbf',
      darkGroupTitleColor: 'rgba(255,255,255,0.45)',
      itemHeight: 40,
    },
    Table: {
      headerBg: '#f7f9fc',
      headerColor: '#4a5568',
      rowHoverBg: '#f0f5ff',
    },
    Card: {
      headerFontSize: 15,
    },
  },
}
