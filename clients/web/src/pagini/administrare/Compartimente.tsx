import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { App, Button, Form, Input, Modal, Space, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { useCompartimente, useSalveazaCompartiment } from '../../api/proiecte'
import type { Compartiment } from '../../api/proiecte'

interface Valori {
  denumire: string
  activ: boolean
}

/** Compartimentele firmei (D-38): pot cere materiale în afara unui șantier. Nu se șterg, se dezactivează. */
export function Compartimente() {
  const { message } = App.useApp()
  const lista = useCompartimente(true)
  const salveaza = useSalveazaCompartiment()
  const [editat, setEditat] = useState<Compartiment | 'nou'>()
  const [form] = Form.useForm<Valori>()

  const deschide = (c: Compartiment | 'nou') => {
    setEditat(c)
    form.setFieldsValue(c === 'nou' ? { denumire: '', activ: true } : { denumire: c.denumire, activ: c.activ })
  }

  const trimite = async (v: Valori) => {
    try {
      await salveaza.mutateAsync({ id: editat === 'nou' ? undefined : editat?.id, date: { denumire: v.denumire.trim(), activ: v.activ } })
      message.success(editat === 'nou' ? `Compartimentul „${v.denumire.trim()}” a fost adăugat.` : 'Compartimentul a fost modificat.')
      setEditat(undefined)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }}>
      <Space style={{ width: '100%', justifyContent: 'space-between' }}>
        <Typography.Text type="secondary">Compartimentele firmei (aprovizionare, mecanizare, birou tehnic, atelier…). La necesarul de materiale se alege un șantier al proiectului sau un compartiment.</Typography.Text>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => deschide('nou')}>Compartiment nou</Button>
      </Space>
      <Table<Compartiment>
        rowKey="id" size="small" pagination={false} loading={lista.isLoading} dataSource={lista.data}
        locale={{ emptyText: 'Niciun compartiment încă.' }}
        columns={[
          { title: 'Denumire', dataIndex: 'denumire' },
          { title: 'Stare', dataIndex: 'activ', width: 120, render: (a: boolean) => (a ? <Tag color="green">activ</Tag> : <Tag>dezactivat</Tag>) },
          { title: '', key: 'e', width: 60, render: (_, c) => <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschide(c)} /> },
        ]}
      />
      <Modal open={!!editat} title={editat === 'nou' ? 'Compartiment nou' : 'Modifică compartimentul'} okText="Salvează" cancelText="Renunță"
        onOk={() => form.submit()} confirmLoading={salveaza.isPending} onCancel={() => setEditat(undefined)} forceRender>
        <Form form={form} layout="vertical" onFinish={trimite}>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true, message: 'Denumirea este obligatorie.' }, { max: 200 }]}>
            <Input placeholder="ex. Mecanizare" />
          </Form.Item>
          <Form.Item name="activ" label="Activ" valuePropName="checked" extra="Compartimentul dezactivat nu mai apare la necesarele noi.">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
