import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, Button, Form, Input, Modal, Popconfirm, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { EroareApi } from '../../api/client'
import { useSalveazaSantier, useSantiere, useStergeSantier } from '../../api/proiecte'
import type { Santier } from '../../api/proiecte'

interface Valori {
  denumire: string
  adresa?: string
  activ: boolean
}

/**
 * Șantierele proiectului (D-38): pe ele se emit necesarele de materiale. Proiectul pornește cu „Șantier principal” (redenumibil) și
 * rămâne mereu cu cel puțin unul activ. Șantierul pus din greșeală se șterge; necesarele lui se mută pe alt șantier al proiectului.
 */
export function SantiereProiect({ proiectId, poateEdita }: { proiectId: string; poateEdita: boolean }) {
  const { message } = App.useApp()
  const santiere = useSantiere(proiectId, true)
  const salveaza = useSalveazaSantier()
  const sterge = useStergeSantier()
  const [deMutat, setDeMutat] = useState<{ santier: Santier; documente: number; mesaj: string }>()
  const [mutaPe, setMutaPe] = useState<string>()
  const alternative = (santiere.data ?? []).filter((x) => x.activ && x.id !== deMutat?.santier.id)

  const stergeSantier = async (s: Santier, tinta?: string) => {
    try {
      await sterge.mutateAsync({ proiectId, id: s.id, mutaPe: tinta })
      const dest = tinta ? santiere.data?.find((x) => x.id === tinta)?.denumire : undefined
      message.success(dest ? `Șantierul „${s.denumire}” a fost șters; necesarele lui sunt acum pe „${dest}”.` : `Șantierul „${s.denumire}” a fost șters.`)
      setDeMutat(undefined)
    } catch (e) {
      const detalii = e instanceof EroareApi ? (e.detalii as { documente?: number } | null) : null
      if (e instanceof EroareApi && e.status === 409 && detalii?.documente) {
        setMutaPe(santiere.data?.find((x) => x.activ && x.id !== s.id)?.id)
        setDeMutat({ santier: s, documente: detalii.documente, mesaj: e.message })
      } else {
        message.error((e as Error).message)
      }
    }
  }
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
        <Typography.Text type="secondary">Punctele de lucru ale proiectului (la un sediu: birouri, bază, depozit). Necesarele de materiale se emit pe unul dintre ele.</Typography.Text>
        {poateEdita && <Button icon={<PlusOutlined />} onClick={() => deschide('nou')}>Șantier nou</Button>}
      </Space>
      <Table<Santier>
        rowKey="id" size="small" pagination={false} loading={santiere.isLoading} dataSource={santiere.data}
        columns={[
          { title: 'Denumire', dataIndex: 'denumire' },
          { title: 'Adresă', dataIndex: 'adresa', render: (a: string | null) => a ?? '—' },
          { title: 'Stare', dataIndex: 'activ', width: 110, render: (a: boolean) => (a ? <Tag color="green">activ</Tag> : <Tag>dezactivat</Tag>) },
          ...(poateEdita ? [{
            title: '', key: 'e', width: 90,
            render: (_: unknown, s: Santier) => (
              <Space size={0}>
                <Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschide(s)} />
                <Popconfirm title={`Ștergi șantierul „${s.denumire}”?`} description="Dacă are necesare de materiale, alegi apoi unde se mută."
                  okText="Șterge" okButtonProps={{ danger: true }} cancelText="Nu" onConfirm={() => stergeSantier(s)}>
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} loading={sterge.isPending && sterge.variables?.id === s.id} />
                </Popconfirm>
              </Space>
            ),
          }] : []),
        ]}
      />
      <Modal open={!!deMutat} title={`Șterge șantierul „${deMutat?.santier.denumire ?? ''}”`} okText="Mută și șterge" okButtonProps={{ danger: true, disabled: !mutaPe }}
        cancelText="Renunță" confirmLoading={sterge.isPending} onCancel={() => setDeMutat(undefined)}
        onOk={() => deMutat && void stergeSantier(deMutat.santier, mutaPe)}>
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Alert type="warning" showIcon title={deMutat?.mesaj} />
          {alternative.length === 0
            ? <Typography.Text type="danger">Proiectul nu are alt șantier activ: adaugă unul întâi.</Typography.Text>
            : (
              <Select style={{ width: '100%' }} value={mutaPe} onChange={setMutaPe} placeholder="Șantierul pe care se mută necesarele"
                options={alternative.map((x) => ({ value: x.id, label: x.adresa ? `${x.denumire} · ${x.adresa}` : x.denumire }))} />
            )}
          <Typography.Text type="secondary">Necesarele (și cele aprobate) trec pe șantierul ales; schimbarea rămâne în jurnalul de modificări.</Typography.Text>
        </Space>
      </Modal>
      <Modal open={!!editat} title={editat === 'nou' ? 'Șantier nou' : 'Modifică șantierul'} okText="Salvează" cancelText="Renunță"
        onOk={() => form.submit()} confirmLoading={salveaza.isPending} onCancel={() => setEditat(undefined)} forceRender>
        <Form form={form} layout="vertical" onFinish={trimite}>
          <Form.Item name="denumire" label="Denumire" rules={[{ required: true, whitespace: true, message: 'Denumirea este obligatorie.' }, { max: 200 }]}>
            <Input placeholder="ex. Tronson 2 – Podari, Corp B" />
          </Form.Item>
          <Form.Item name="adresa" label="Adresă" rules={[{ max: 500 }]}>
            <Input.TextArea autoSize={{ minRows: 1, maxRows: 3 }} />
          </Form.Item>
          <Form.Item name="activ" label="Activ" valuePropName="checked" extra="Șantierul dezactivat nu mai apare la necesarele noi; cele vechi îl păstrează. Proiectul rămâne cu cel puțin un șantier activ.">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
