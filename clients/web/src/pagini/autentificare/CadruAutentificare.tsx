import { CheckCircleFilled } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'

const an = new Date().getFullYear()

const puncte = [
  'Devize, oferte și versiuni, legate de articolul de deviz',
  'Situații de lucrări, facturi și e-Factura',
  'Date izolate pe firmă, cu drepturi pe proiect',
]

/** Cadrul paginilor de autentificare: panou de prezentare în stânga, formular în dreapta. */
export function CadruAutentificare({ children }: { children: ReactNode }) {
  return (
    <div style={{ minHeight: '100vh', display: 'flex', background: '#f3f5f8' }}>
      <div
        style={{
          flex: '0 0 44%',
          background: 'linear-gradient(160deg, #14294a 0%, #1a3a6b 60%, #1f5fbf 140%)',
          color: '#fff',
          padding: '48px 56px',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
        }}
        className="panou-prezentare"
      >
        <Flex align="center" gap={12}>
          <img src="/favicon.svg" alt="" width={40} height={40} />
          <div>
            <div style={{ fontWeight: 700, fontSize: 20 }}>ERP Construcții</div>
            <div style={{ opacity: 0.6, fontSize: 13 }}>Management proiecte</div>
          </div>
        </Flex>

        <div style={{ maxWidth: 440 }}>
          <Typography.Title level={2} style={{ color: '#fff', marginBottom: 16 }}>
            Proiectul este axa costurilor.
          </Typography.Title>
          <Typography.Paragraph style={{ color: 'rgba(255,255,255,0.75)', fontSize: 15 }}>
            De la ofertă la situația lunară: fiecare cost și venit se leagă de proiect și de articolul de deviz.
          </Typography.Paragraph>
          <Flex vertical gap={12} style={{ marginTop: 28 }}>
            {puncte.map((p) => (
              <Flex key={p} gap={10} align="center" style={{ color: 'rgba(255,255,255,0.9)' }}>
                <CheckCircleFilled style={{ color: '#f5a623' }} /> {p}
              </Flex>
            ))}
          </Flex>
        </div>

        <div style={{ opacity: 0.5, fontSize: 12 }}>© {an} ERP Construcții</div>
      </div>

      <Flex flex={1} align="center" justify="center" style={{ padding: 32 }}>
        <div style={{ width: '100%', maxWidth: 400 }}>{children}</div>
      </Flex>
    </div>
  )
}
