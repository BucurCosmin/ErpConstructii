import { PlusOutlined } from '@ant-design/icons'
import { App, Button, Flex, Form, Input, Modal, Table, Tag, Typography } from 'antd'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useCreeazaFirma, useFirme } from '../../api/administrare'
import { chei } from '../../api/interogari'
import type { FirmaAdmin } from '../../api/tipuri'

/** Toate firmele (doar administratorul de sistem). La creare, firma primește rolurile predefinite. */
export function Firme() {
  const { data, isLoading } = useFirme(true)
  const creeaza = useCreeazaFirma()
  const qc = useQueryClient()
  const { message } = App.useApp()
  const [deschis, setDeschis] = useState(false)
  const [form] = Form.useForm<{ denumire: string; cui: string }>()

  const salveaza = async () => {
    const v = await form.validateFields()
    try {
      const f = await creeaza.mutateAsync({ denumire: v.denumire, cui: v.cui || null })
      await qc.invalidateQueries({ queryKey: chei.profil }) // apare în selectorul de firmă
      message.success(`Firma ${f.denumire} a fost creată. Ești administratorul ei; o poți alege din selectorul de firmă.`)
      form.resetFields()
      setDeschis(false)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <>
      <Flex justify="space-between" align="center" style={{ marginBlock: 8 }}>
        <Typography.Text type="secondary">
          La creare, firma primește rolurile predefinite, iar tu devii administratorul ei.
        </Typography.Text>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setDeschis(true)}>
          Firmă nouă
        </Button>
      </Flex>
      <Table<FirmaAdmin>
        rowKey="id"
        loading={isLoading}
        dataSource={data}
        pagination={false}
        columns={[
          { title: 'Denumire', dataIndex: 'denumire', render: (v) => <Typography.Text strong>{v}</Typography.Text> },
          { title: 'CUI', dataIndex: 'cui', width: 160, render: (v) => v ?? '—' },
          { title: 'Utilizatori cu acces', dataIndex: 'utilizatori', width: 180, align: 'right' },
          {
            title: 'Stare',
            dataIndex: 'activa',
            width: 120,
            render: (a: boolean) => (a ? <Tag color="green">Activă</Tag> : <Tag>Inactivă</Tag>),
          },
        ]}
      />
      <Modal title="Firmă nouă" open={deschis} onCancel={() => setDeschis(false)} onOk={salveaza} okText="Creează" cancelText="Renunță" confirmLoading={creeaza.isPending} destroyOnHidden>
        <Form form={form} layout="vertical" requiredMark="optional">
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }, { max: 300 }]}>
            <Input placeholder="Construct Exemplu SRL" />
          </Form.Item>
          <Form.Item name="cui" label="CUI" rules={[{ max: 20 }]}>
            <Input placeholder="RO12345678" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
