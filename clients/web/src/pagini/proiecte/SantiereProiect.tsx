import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { App, Button, Form, Input, Modal, Space, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { useSalveazaSantier, useSantiere } from '../../api/proiecte'
import type { Santier } from '../../api/proiecte'

interface Valori {
  denumire: string
  adresa?: string
  activ: boolean
}

/** Șantierele proiectului (D-38): pe ele se emit necesarele de materiale. Nu se șterg, se dezactivează. */
export function SantiereProiect({ proiectId, poateEdita }: { proiectId: string; poateEdita: boolean }) {
  const { message } = App.useApp()
  const santiere = useSantiere(proiectId, true)
  const salveaza = useSalveazaSantier()
  const [editat, setEditat] = useState<Santier | 'nou'>()
  const [form] = Form.useForm<Valori>()

  const deschide = (s: Santier | 'nou') => {
    setEditat(s)
    form.setFieldsValue(s === 'nou' ? { denumire: '', adresa: undefined, activ: true } : { denumire: s.denumire, adresa: s.adresa ?? undefined, activ: s.activ })
  }

  const trimite = async (v: Valori) => {
    try {
      await salveaza.mutateAsync({ proiectId, id: editat === 'nou' ? undefined : editat?.id, date: { denumire: v.denumire.trim(), adresa: v.adresa?.trim() || null, activ: v.activ } })
      message.success(editat === 'nou' ? `Șantierul „${v.denumire.trim()}” a fost adăugat.` : 'Șantierul a fost modificat.')
      setEditat(undefined)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }}>
      <Space style={{ width: '100%', justifyContent: 'space-between' }}>
        <Typography.Text type="secondary">Punctele de lucru ale proiectului. Necesarele de materiale se emit pe un șantier sau pe un compartiment al firmei.</Typography.Text>
        {poateEdita && <Button icon={<PlusOutlined />} onClick={() => deschide('nou')}>Șantier nou</Button>}
      </Space>
      <Table<Santier>
        rowKey="id" size="small" pagination={false} loading={santiere.isLoading} dataSource={santiere.data}
        columns={[
          { title: 'Denumire', dataIndex: 'denumire' },
          { title: 'Adresă', dataIndex: 'adresa', render: (a: string | null) => a ?? '—' },
          { title: 'Stare', dataIndex: 'activ', width: 110, render: (a: boolean) => (a ? <Tag color="green">activ</Tag> : <Tag>dezactivat</Tag>) },
          ...(poateEdita ? [{ title: '', key: 'e', width: 60, render: (_: unknown, s: Santier) => <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschide(s)} /> }] : []),
        ]}
      />
      <Modal open={!!editat} title={editat === 'nou' ? 'Șantier nou' : 'Modifică șantierul'} okText="Salvează" cancelText="Renunță"
        onOk={() => form.submit()} confirmLoading={salveaza.isPending} onCancel={() => setEditat(undefined)} forceRender>
        <Form form={form} layout="vertical" onFinish={trimite}>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true, message: 'Denumirea este obligatorie.' }, { max: 200 }]}>
            <Input placeholder="ex. Tronson 2 – Podari, Corp B" />
          </Form.Item>
          <Form.Item name="adresa" label="Adresă" rules={[{ max: 500 }]}>
            <Input.TextArea autoSize={{ minRows: 1, maxRows: 3 }} />
          </Form.Item>
          <Form.Item name="activ" label="Activ" valuePropName="checked" extra="Șantierul dezactivat nu mai apare la necesarele noi; cele vechi îl păstrează.">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
