import { Card, Flex, Result, Tag, Typography } from 'antd'
import type { ModulAplicatie } from '../layout/module'
import { AntetPagina } from '../layout/AntetPagina'

/** Pagina unui modul planificat: ce va face și ce cerințe acoperă (docs/cerinte.md). */
export function ModulInDezvoltare({ modul }: { modul: ModulAplicatie }) {
  return (
    <>
      <AntetPagina titlu={modul.eticheta} cale={[{ titlu: 'Acasă', la: '/' }, { titlu: modul.eticheta }]} />
      <Card>
        <Result
          icon={<div style={{ fontSize: 56, opacity: 0.85 }}>{modul.icon}</div>}
          title={`Modulul „${modul.eticheta}” este în dezvoltare`}
          subTitle={
            <Typography.Paragraph type="secondary" style={{ maxWidth: 640, margin: '0 auto' }}>
              {modul.descriere}
            </Typography.Paragraph>
          }
          extra={
            <Flex vertical align="center" gap={12}>
              {modul.faza && <Tag color="blue">Planificat: {modul.faza}</Tag>}
              {modul.cerinte && (
                <Flex gap={6} wrap justify="center">
                  {modul.cerinte.map((c) => (
                    <Tag key={c}>{c}</Tag>
                  ))}
                </Flex>
              )}
            </Flex>
          }
        />
      </Card>
    </>
  )
}
