import { App, Button, Form, Input, Skeleton } from 'antd'
import { useEffect } from 'react'
import { useFirmaActiva, useModificaFirma } from '../../api/administrare'
import { chei, usePermisiuni } from '../../api/interogari'
import { useQueryClient } from '@tanstack/react-query'

/** Datele firmei active. Setările (cote, serii de numerotare) vin în modulul Setări (backlog). */
export function Firma() {
  const { data, isLoading } = useFirmaActiva()
  const modifica = useModificaFirma()
  const permisiuni = usePermisiuni()
  const qc = useQueryClient()
  const { message } = App.useApp()
  const [form] = Form.useForm<{ denumire: string; cui: string }>()
  const poateEdita = permisiuni.data?.includes('administrare.editare') ?? false

  useEffect(() => {
    if (data) form.setFieldsValue({ denumire: data.denumire, cui: data.cui ?? '' })
  }, [data, form])

  if (isLoading) return <Skeleton active />

  return (
    <Form
      form={form}
      layout="vertical"
      style={{ maxWidth: 520, marginTop: 8 }}
      disabled={!poateEdita}
      onFinish={async (v) => {
        try {
          await modifica.mutateAsync({ denumire: v.denumire, cui: v.cui || null })
          await qc.invalidateQueries({ queryKey: chei.profil }) // denumirea din selectorul de firmă
          message.success('Datele firmei au fost salvate.')
        } catch (e) {
          message.error((e as Error).message)
        }
      }}
    >
      <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
        <Input />
      </Form.Item>
      <Form.Item name="cui" label="CUI" rules={[{ max: 20 }]}>
        <Input placeholder="RO12345678" />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={modifica.isPending}>
        Salvează
      </Button>
    </Form>
  )
}
