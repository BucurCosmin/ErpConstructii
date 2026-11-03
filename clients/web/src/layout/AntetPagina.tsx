import { Breadcrumb, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'

interface Props {
  titlu: ReactNode
  subtitlu?: ReactNode
  cale?: { titlu: string; la?: string }[]
  actiuni?: ReactNode
  extra?: ReactNode
}

/** Antetul standard al unei pagini: fir de navigare, titlu, acțiuni. */
export function AntetPagina({ titlu, subtitlu, cale, actiuni, extra }: Props) {
  return (
    <div style={{ marginBottom: 20 }}>
      {cale && (
        <Breadcrumb
          style={{ marginBottom: 8 }}
          items={cale.map((c) => ({ title: c.la ? <Link to={c.la}>{c.titlu}</Link> : c.titlu }))}
        />
      )}
      <Flex justify="space-between" align="flex-start" gap={16} wrap>
        <div style={{ minWidth: 0 }}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {titlu}
          </Typography.Title>
          {subtitlu && (
            <Typography.Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
              {subtitlu}
            </Typography.Text>
          )}
        </div>
        {actiuni && <Flex gap={8}>{actiuni}</Flex>}
      </Flex>
      {extra}
    </div>
  )
}
