import { App, Button, DatePicker, Drawer, Form, Input, Select, Space, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { useCreeazaUtilizator, useRoluri } from '../../api/administrare'
import { EroareApi } from '../../api/client'
import { useProfil, useProiecte } from '../../api/interogari'
import type { TipUtilizator } from '../../api/tipuri'
import { tipuriUtilizator } from '../../util/format'
import { ParolaTemporara } from './ParolaTemporara'

interface Valori {
  email: string
  nume: string
  tip: TipUtilizator
  rolId: string
  proiectId?: string
  valabilDe?: Dayjs
}

interface Props {
  deschis: boolean
  laInchidere: () => void
  laCreare: (id: string) => void
}

export function UtilizatorNou({ deschis, laInchidere, laCreare }: Props) {
  const [form] = Form.useForm<Valori>()
  const roluri = useRoluri()
  const proiecte = useProiecte()
  const profil = useProfil()
  const creeaza = useCreeazaUtilizator()
  const { message } = App.useApp()
  const [parola, setParola] = useState<{ parola: string; pentru: string; id: string } | null>(null)

  const tipuriPermise = (Object.keys(tipuriUtilizator) as TipUtilizator[]).filter(
    (t) => t !== 'AdministratorSistem' || profil.data?.tip === 'AdministratorSistem',
  )

  const salveaza = async () => {
    const v = await form.validateFields()
    try {
      const r = await creeaza.mutateAsync({
        email: v.email,
        nume: v.nume,
        tip: v.tip,
        rolId: v.rolId,
        proiectId: v.proiectId ?? null,
        valabilDe: v.valabilDe?.format('YYYY-MM-DD') ?? null,
      })
      form.resetFields()
      laInchidere()
      if (r.parolaTemporara) {
        setParola({ parola: r.parolaTemporara, pentru: v.nume, id: r.id })
      } else {
        message.success(`${v.nume} avea deja cont; a primit acces la această firmă.`)
        laCreare(r.id)
      }
    } catch (e) {
      if (e instanceof EroareApi && e.status === 409) form.setFields([{ name: 'email', errors: [e.message] }])
      else message.error((e as Error).message)
    }
  }

  return (
    <>
      <Drawer
        title="Utilizator nou"
        open={deschis}
        onClose={laInchidere}
        size={520}
        destroyOnHidden
        extra={
          <Space>
            <Button onClick={laInchidere}>Renunță</Button>
            <Button type="primary" loading={creeaza.isPending} onClick={salveaza}>
              Creează
            </Button>
          </Space>
        }
      >
        <Typography.Paragraph type="secondary">
          Dacă adresa de email are deja cont (la altă firmă), utilizatorul primește doar acces la această firmă, cu rolul ales.
        </Typography.Paragraph>
        <Form form={form} layout="vertical" requiredMark="optional" initialValues={{ tip: 'Intern', valabilDe: dayjs() }}>
          <Form.Item name="email" label="Email" rules={[{ required: true, type: 'email', message: 'Introdu o adresă de email validă.' }]}>
            <Input placeholder="prenume.nume@firma.ro" />
          </Form.Item>
          <Form.Item name="nume" label="Nume complet" rules={[{ required: true, message: 'Numele este obligatoriu.' }, { max: 200 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="tip" label="Tip utilizator" rules={[{ required: true }]}>
            <Select
              options={tipuriPermise.map((t) => ({
                value: t,
                label: (
                  <div>
                    <div>{tipuriUtilizator[t].eticheta}</div>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {tipuriUtilizator[t].descriere}
                    </Typography.Text>
                  </div>
                ),
              }))}
              optionLabelProp="value"
              labelRender={({ value }) => tipuriUtilizator[value as TipUtilizator]?.eticheta}
            />
          </Form.Item>
          <Form.Item name="rolId" label="Rol" rules={[{ required: true, message: 'Alege rolul.' }]}>
            <Select
              loading={roluri.isLoading}
              showSearch={{ optionFilterProp: 'label' }}
              options={roluri.data?.map((r) => ({ value: r.id, label: r.denumire }))}
            />
          </Form.Item>
          <Form.Item
            name="proiectId"
            label="Doar pe proiectul"
            tooltip="Lasă gol pentru acces pe toată firma. Cu un proiect ales, drepturile rolului se aplică doar acolo."
          >
            <Select
              allowClear
              placeholder="Toată firma"
              showSearch={{ optionFilterProp: 'label' }}
              options={proiecte.data?.map((p) => ({ value: p.id, label: `${p.cod} – ${p.denumire}` }))}
            />
          </Form.Item>
          <Form.Item name="valabilDe" label="Valabil de la">
            <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Drawer>

      <ParolaTemporara
        parola={parola?.parola ?? null}
        pentru={parola?.pentru ?? ''}
        laInchidere={() => {
          const id = parola?.id
          setParola(null)
          if (id) laCreare(id)
        }}
      />
    </>
  )
}
