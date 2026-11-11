import { HistoryOutlined } from '@ant-design/icons'
import { Checkbox, Form, Tag, Tooltip } from 'antd'

/**
 * Bifa „document din urmă” (D-45) din ecranele de import: documentele vechi, aduse din arhivă, nu au neapărat în sistem
 * documentele de dinaintea lor (necesarul, nota de comandă, contractul); legăturile se pun ulterior, când există.
 */
export function CampDinUrma({ explicatie, disabled }: { explicatie: string; disabled?: boolean }) {
  return (
    <Form.Item name="dinUrma" valuePropName="checked" extra={explicatie} style={{ marginBottom: 12 }}>
      <Checkbox disabled={disabled}>
        <HistoryOutlined /> Document din urmă (import din arhivă)
      </Checkbox>
    </Form.Item>
  )
}

/** Eticheta din liste pentru documentele importate din urmă. */
export function TagDinUrma() {
  return (
    <Tooltip title="Document din urmă: importat din arhivă; documentele de dinaintea lui pot lipsi din sistem.">
      <Tag icon={<HistoryOutlined />} style={{ marginInlineEnd: 0 }}>din urmă</Tag>
    </Tooltip>
  )
}
