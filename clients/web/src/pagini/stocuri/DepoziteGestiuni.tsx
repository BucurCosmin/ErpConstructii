import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { App, Button, Card, Drawer, Empty, Form, Input, Modal, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { useCreeazaDepozit, useCreeazaGestiune, useDepozite, useGestionari, useModificaDepozit, useModificaGestiune } from '../../api/stocuri'
import type { Depozit, Gestiune } from '../../api/stocuri'

type FormDepozit = { cod: string; denumire: string; adresa?: string; activ: boolean }
type FormGestiune = { cod: string; denumire: string; gestionarId?: string | null; activa: boolean }

/**
 * Depozitele firmei (locurile fizice) și gestiunile din fiecare, cu gestionarul care răspunde de ele (D-43).
 * Stocul se ține pe gestiune; recepțiile și transferurile aleg gestiunea.
 */
export function DepoziteGestiuni({ onInchide, poateEdita }: { onInchide: () => void; poateEdita: boolean }) {
  const { message } = App.useApp()
  const depozite = useDepozite()
  const gestionari = useGestionari()
  const creeazaDepozit = useCreeazaDepozit()
  const modificaDepozit = useModificaDepozit()
  const creeazaGestiune = useCreeazaGestiune()
  const modificaGestiune = useModificaGestiune()
  const [depozitEditat, setDepozitEditat] = useState<Depozit | 'nou'>()
  const [gestiuneEditata, setGestiuneEditata] = useState<{ depozit: Depozit; gestiune?: Gestiune }>()
  const [formDepozit] = Form.useForm<FormDepozit>()
  const [formGestiune] = Form.useForm<FormGestiune>()

  const optiuniGestionari = (gestionari.data ?? []).map((u) => ({ value: u.id, label: `${u.nume} (${u.email})` }))

  const deschideDepozit = (d: Depozit | 'nou') => {
    setDepozitEditat(d)
    formDepozit.setFieldsValue(d === 'nou' ? { cod: '', denumire: '', adresa: '', activ: true } : { cod: d.cod, denumire: d.denumire, adresa: d.adresa ?? '', activ: d.activ })
  }

  const deschideGestiune = (depozit: Depozit, gestiune?: Gestiune) => {
    setGestiuneEditata({ depozit, gestiune })
    formGestiune.setFieldsValue(gestiune
      ? { cod: gestiune.cod, denumire: gestiune.denumire, gestionarId: gestiune.gestionarId, activa: gestiune.activa }
      : { cod: '', denumire: '', gestionarId: null, activa: true })
  }

  const salveazaDepozit = async (v: FormDepozit) => {
    try {
      if (depozitEditat === 'nou') await creeazaDepozit.mutateAsync({ cod: v.cod, denumire: v.denumire, adresa: v.adresa || null })
      else if (depozitEditat) await modificaDepozit.mutateAsync({ id: depozitEditat.id, cod: depozitEditat.cod, denumire: v.denumire, adresa: v.adresa || null, activ: v.activ })
      message.success('Depozitul a fost salvat.')
      setDepozitEditat(undefined)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const salveazaGestiune = async (v: FormGestiune) => {
    if (!gestiuneEditata) return
    try {
      const { depozit, gestiune } = gestiuneEditata
      if (gestiune) await modificaGestiune.mutateAsync({ id: gestiune.id, denumire: v.denumire, gestionarId: v.gestionarId ?? null, activa: v.activa })
      else await creeazaGestiune.mutateAsync({ depozitId: depozit.id, cod: v.cod, denumire: v.denumire, gestionarId: v.gestionarId ?? null })
      message.success('Gestiunea a fost salvată.')
      setGestiuneEditata(undefined)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Drawer open onClose={onInchide} size={900} title="Depozite și gestiuni"
      extra={poateEdita && <Button icon={<PlusOutlined />} onClick={() => deschideDepozit('nou')}>Depozit nou</Button>}>
      <Typography.Paragraph type="secondary">
        Un depozit (locul fizic) se împarte în gestiuni, fiecare cu gestionarul care răspunde de ea. Stocul se ține pe gestiune:
        același material poate fi în mai multe gestiuni ale aceluiași depozit.
      </Typography.Paragraph>
      <Space direction="vertical" size="middle" style={{ width: '100%' }}>
        {(depozite.data ?? []).map((d) => (
          <Card key={d.id} size="small" loading={depozite.isLoading}
            title={<Space>{`${d.cod} — ${d.denumire}`}{!d.activ && <Tag>inactiv</Tag>}</Space>}
            extra={poateEdita && (
              <Space>
                <Button size="small" icon={<PlusOutlined />} disabled={!d.activ} onClick={() => deschideGestiune(d)}>Gestiune nouă</Button>
                <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschideDepozit(d)} />
              </Space>
            )}>
            {d.adresa && <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>{d.adresa}</Typography.Paragraph>}
            {d.gestiuni.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nicio gestiune" /> : (
              <Table<Gestiune> size="small" rowKey="id" pagination={false} dataSource={d.gestiuni}
                columns={[
                  { title: 'Cod', dataIndex: 'cod', width: 110 },
                  { title: 'Denumire', dataIndex: 'denumire' },
                  { title: 'Gestionar', dataIndex: 'gestionarNume', width: 220, render: (n: string | null) => n ?? <Typography.Text type="secondary">nealocat</Typography.Text> },
                  { title: 'Stare', dataIndex: 'activa', width: 90, render: (a: boolean) => (a ? <Tag color="green">activă</Tag> : <Tag>inactivă</Tag>) },
                  { title: '', width: 50, render: (_, g) => poateEdita && <Button type="text" icon={<EditOutlined />} onClick={() => deschideGestiune(d, g)} /> },
                ]} />
            )}
          </Card>
        ))}
      </Space>

      <Modal open={!!depozitEditat} title={depozitEditat === 'nou' ? 'Depozit nou' : `Depozit ${depozitEditat?.cod ?? ''}`} okText="Salvează" cancelText="Renunță"
        onCancel={() => setDepozitEditat(undefined)} onOk={() => formDepozit.submit()} confirmLoading={creeazaDepozit.isPending || modificaDepozit.isPending} destroyOnHidden>
        <Form form={formDepozit} layout="vertical" name="depozit" onFinish={salveazaDepozit}>
          <Form.Item name="cod" label="Cod" rules={[{ required: true, whitespace: true }]}><Input maxLength={20} placeholder="DEP-02" disabled={depozitEditat !== 'nou'} /></Form.Item>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true }]}><Input maxLength={200} placeholder="Depozit Cernavodă" /></Form.Item>
          <Form.Item name="adresa" label="Adresa"><Input maxLength={300} /></Form.Item>
          {depozitEditat !== 'nou' && <Form.Item name="activ" label="Activ" valuePropName="checked"><Switch /></Form.Item>}
        </Form>
      </Modal>

      <Modal open={!!gestiuneEditata} okText="Salvează" cancelText="Renunță"
        title={gestiuneEditata?.gestiune ? `Gestiunea ${gestiuneEditata.gestiune.cod}` : `Gestiune nouă în ${gestiuneEditata?.depozit.denumire ?? ''}`}
        onCancel={() => setGestiuneEditata(undefined)} onOk={() => formGestiune.submit()} confirmLoading={creeazaGestiune.isPending || modificaGestiune.isPending} destroyOnHidden>
        <Form form={formGestiune} layout="vertical" name="gestiune" onFinish={salveazaGestiune}>
          <Form.Item name="cod" label="Cod" rules={[{ required: true, whitespace: true }]}><Input maxLength={20} placeholder="G5" disabled={!!gestiuneEditata?.gestiune} /></Form.Item>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true }]}><Input maxLength={200} placeholder="Gestiunea G5" /></Form.Item>
          <Form.Item name="gestionarId" label="Gestionar" extra="Utilizatorul care răspunde de gestiune.">
            <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Nealocat" options={optiuniGestionari} loading={gestionari.isLoading} />
          </Form.Item>
          {gestiuneEditata?.gestiune && <Form.Item name="activa" label="Activă" valuePropName="checked"><Switch /></Form.Item>}
        </Form>
      </Modal>
    </Drawer>
  )
}
