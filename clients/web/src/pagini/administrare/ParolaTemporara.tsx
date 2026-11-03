import { CopyOutlined } from '@ant-design/icons'
import { Alert, App, Button, Flex, Modal, Typography } from 'antd'

interface Props {
  parola: string | null
  pentru: string
  laInchidere: () => void
}

/** Afișează o singură dată parola temporară, ca administratorul s-o transmită utilizatorului. */
export function ParolaTemporara({ parola, pentru, laInchidere }: Props) {
  const { message } = App.useApp()
  return (
    <Modal
      open={!!parola}
      title="Parolă temporară"
      onCancel={laInchidere}
      footer={
        <Button type="primary" onClick={laInchidere}>
          Am transmis parola
        </Button>
      }
      maskClosable={false}
    >
      <Typography.Paragraph>
        Transmite parola către <strong>{pentru}</strong> pe un canal sigur (telefonic sau în persoană, nu în același email cu
        adresa contului). La primul login va fi obligat să o schimbe.
      </Typography.Paragraph>
      <Flex
        align="center"
        justify="space-between"
        style={{ padding: '12px 16px', background: '#f7f9fc', border: '1px solid #e5e7eb', borderRadius: 8 }}
      >
        <Typography.Text style={{ fontFamily: 'Consolas, monospace', fontSize: 20, letterSpacing: 1 }}>{parola}</Typography.Text>
        <Button
          icon={<CopyOutlined />}
          onClick={() => navigator.clipboard.writeText(parola ?? '').then(() => message.success('Parola a fost copiată.'))}
        >
          Copiază
        </Button>
      </Flex>
      <Alert type="warning" showIcon style={{ marginTop: 16 }} title="Parola nu va mai fi afișată după închiderea ferestrei." />
    </Modal>
  )
}
