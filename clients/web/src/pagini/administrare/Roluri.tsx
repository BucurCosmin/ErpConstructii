import { DeleteOutlined, PlusOutlined, RollbackOutlined, SaveOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  Badge,
  Button,
  Card,
  Checkbox,
  Col,
  Empty,
  Flex,
  Form,
  Input,
  List,
  Modal,
  Popconfirm,
  Row,
  Select,
  Skeleton,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd'
import { useMemo, useState } from 'react'
import { useCatalogPermisiuni, useCreeazaRol, useModificaRol, useRestaureazaRol, useRoluri, useStergeRol } from '../../api/administrare'
import { usePermisiuni } from '../../api/interogari'
import type { CatalogPermisiuni, ModulCatalog, RolFirma } from '../../api/tipuri'

const actiuni = ['vizualizare', 'creare', 'editare', 'anulare', 'aprobare', 'export'] as const
const eticheteActiuni: Record<(typeof actiuni)[number], string> = {
  vizualizare: 'Vizualizare',
  creare: 'Creare',
  editare: 'Editare',
  anulare: 'Anulare',
  aprobare: 'Aprobare',
  export: 'Export',
}

/** Roluri și permisiuni (REQ-DRP-01/02): rolurile predefinite se pot ajusta, iar rolurile proprii se pot crea. */
export function Roluri() {
  const roluri = useRoluri()
  const catalog = useCatalogPermisiuni()
  const permisiuniMele = usePermisiuni()
  const [selectat, setSelectat] = useState<string | null>(null)
  const [nouDeschis, setNouDeschis] = useState(false)
  const poateEdita = permisiuniMele.data?.includes('administrare.editare') ?? false

  const rol = roluri.data?.find((r) => r.id === selectat) ?? roluri.data?.[0]

  if (roluri.isLoading || catalog.isLoading) return <Skeleton active />

  return (
    <>
      <Row gutter={16} style={{ marginTop: 8 }}>
        <Col xs={24} lg={7}>
          <Card
            size="small"
            title={`Roluri (${roluri.data?.length ?? 0})`}
            extra={
              <Button size="small" type="primary" icon={<PlusOutlined />} disabled={!poateEdita} onClick={() => setNouDeschis(true)}>
                Rol nou
              </Button>
            }
            styles={{ body: { padding: 0, maxHeight: 640, overflow: 'auto' } }}
          >
            <List
              dataSource={roluri.data}
              renderItem={(r) => (
                <List.Item
                  onClick={() => setSelectat(r.id)}
                  style={{
                    cursor: 'pointer',
                    paddingInline: 16,
                    background: r.id === rol?.id ? '#eef4ff' : undefined,
                    borderInlineStart: r.id === rol?.id ? '3px solid #1f5fbf' : '3px solid transparent',
                  }}
                >
                  <List.Item.Meta
                    title={r.denumire}
                    description={
                      <Space size={4}>
                        {r.predefinit ? <Tag>predefinit</Tag> : <Tag color="purple">propriu</Tag>}
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {r.permisiuni.length} permisiuni
                        </Typography.Text>
                      </Space>
                    }
                  />
                  <Badge count={r.utilizatori} showZero color={r.utilizatori ? '#1f5fbf' : '#d9d9d9'} title="Utilizatori cu acest rol" />
                </List.Item>
              )}
            />
          </Card>
        </Col>

        <Col xs={24} lg={17}>
          {!rol || !catalog.data ? (
            <Empty />
          ) : (
            // Cheia include starea din server: după salvare sau la alt rol, editorul pornește de la valorile noi.
            <EditorRol
              key={`${rol.id}|${rol.denumire}|${rol.permisiuni.join(',')}`}
              rol={rol}
              catalog={catalog.data}
              laStergere={() => setSelectat(null)}
            />
          )}
        </Col>
      </Row>

      <RolNou deschis={nouDeschis} laInchidere={() => setNouDeschis(false)} laCreare={(id) => setSelectat(id)} />
    </>
  )
}

/** Editorul unui rol: denumire și matricea de permisiuni, cu stare locală până la salvare. */
function EditorRol({ rol, catalog, laStergere }: { rol: RolFirma; catalog: CatalogPermisiuni; laStergere: () => void }) {
  const permisiuniMele = usePermisiuni()
  const modifica = useModificaRol()
  const restaureaza = useRestaureazaRol()
  const sterge = useStergeRol()
  const { message } = App.useApp()
  const [denumire, setDenumire] = useState(rol.denumire)
  const [bifate, setBifate] = useState<Set<string>>(() => new Set(rol.permisiuni))
  const poateEdita = permisiuniMele.data?.includes('administrare.editare') ?? false
  const poateAnula = permisiuniMele.data?.includes('administrare.anulare') ?? false

  const modificat = useMemo(
    () => !!rol && (denumire !== rol.denumire || bifate.size !== rol.permisiuni.length || rol.permisiuni.some((p) => !bifate.has(p))),
    [rol, denumire, bifate],
  )

  const comuta = (coduri: string[], valoare: boolean) =>
    setBifate((v) => {
      const nou = new Set(v)
      coduri.forEach((c) => (valoare ? nou.add(c) : nou.delete(c)))
      return nou
    })

  const executa = async (fn: () => Promise<unknown>, succes: string) => {
    try {
      await fn()
      message.success(succes)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
            <Card
              size="small"
              title={
                <Flex gap={8} align="center">
                  <Input
                    value={denumire}
                    onChange={(e) => setDenumire(e.target.value)}
                    disabled={!poateEdita}
                    variant="borderless"
                    style={{ fontWeight: 600, fontSize: 16, paddingInline: 0, maxWidth: 360 }}
                  />
                  <Typography.Text type="secondary" code>
                    {rol.cod}
                  </Typography.Text>
                </Flex>
              }
              extra={
                <Space>
                  {rol.predefinit && (
                    <Popconfirm
                      title="Restaurezi permisiunile implicite?"
                      description="Rolul revine la matricea din documentul de proiect."
                      okText="Restaurează"
                      cancelText="Renunță"
                      disabled={!poateEdita}
                      onConfirm={() => executa(() => restaureaza.mutateAsync(rol.id), 'Rolul a revenit la permisiunile implicite.')}
                    >
                      <Button icon={<RollbackOutlined />} disabled={!poateEdita}>
                        Implicit
                      </Button>
                    </Popconfirm>
                  )}
                  <Popconfirm
                    title="Ștergi rolul?"
                    description="Se poate șterge doar un rol care nu a fost atribuit niciodată."
                    okText="Șterge"
                    okButtonProps={{ danger: true }}
                    cancelText="Renunță"
                    disabled={!poateAnula}
                    onConfirm={() =>
                      executa(async () => {
                        await sterge.mutateAsync(rol.id)
                        laStergere()
                      }, 'Rolul a fost șters.')
                    }
                  >
                    <Button danger icon={<DeleteOutlined />} disabled={!poateAnula} />
                  </Popconfirm>
                  <Button
                    type="primary"
                    icon={<SaveOutlined />}
                    disabled={!poateEdita || !modificat}
                    loading={modifica.isPending}
                    onClick={() =>
                      executa(() => modifica.mutateAsync({ id: rol.id, denumire, permisiuni: [...bifate] }), 'Rolul a fost salvat. Drepturile se aplică imediat.')
                    }
                  >
                    Salvează
                  </Button>
                </Space>
              }
            >
              {rol.utilizatori > 0 && modificat && (
                <Alert
                  type="warning"
                  showIcon
                  style={{ marginBottom: 12 }}
                  title={`Modificările se aplică imediat celor ${rol.utilizatori} utilizatori cu acest rol.`}
                />
              )}

              <Table<ModulCatalog>
                rowKey="cod"
                size="small"
                pagination={false}
                dataSource={catalog.module}
                columns={[
                  {
                    title: 'Modul',
                    render: (_, m) => {
                      const toate = m.permisiuni.map((p) => p.cod)
                      const nr = toate.filter((c) => bifate.has(c)).length
                      return (
                        <Checkbox
                          checked={nr === toate.length}
                          indeterminate={nr > 0 && nr < toate.length}
                          disabled={!poateEdita}
                          onChange={(e) => comuta(toate, e.target.checked)}
                        >
                          <Typography.Text strong={nr > 0}>{m.denumire}</Typography.Text>
                        </Checkbox>
                      )
                    },
                  },
                  ...actiuni.map((a) => ({
                    title: eticheteActiuni[a],
                    width: 96,
                    align: 'center' as const,
                    render: (_: unknown, m: ModulCatalog) => {
                      const cod = `${m.cod}.${a}`
                      return <Checkbox checked={bifate.has(cod)} disabled={!poateEdita} onChange={(e) => comuta([cod], e.target.checked)} />
                    },
                  })),
                ]}
              />

              <Typography.Title level={5} style={{ marginTop: 20 }}>
                Date sensibile
              </Typography.Title>
              <Typography.Paragraph type="secondary" style={{ marginTop: -4 }}>
                Fără aceste permisiuni, valorile apar mascate, chiar dacă rolul vede modulul.
              </Typography.Paragraph>
              <Space size={24} wrap>
                {catalog.dateSensibile.map((p) => (
                  <Checkbox key={p.cod} checked={bifate.has(p.cod)} disabled={!poateEdita} onChange={(e) => comuta([p.cod], e.target.checked)}>
                    {p.denumire}
                  </Checkbox>
                ))}
              </Space>
            </Card>
  )
}

function RolNou({ deschis, laInchidere, laCreare }: { deschis: boolean; laInchidere: () => void; laCreare: (id: string) => void }) {
  const [form] = Form.useForm<{ denumire: string; cod: string; sursa?: string }>()
  const roluri = useRoluri()
  const creeaza = useCreeazaRol()
  const { message } = App.useApp()

  const salveaza = async () => {
    const v = await form.validateFields()
    try {
      const sursa = roluri.data?.find((r) => r.id === v.sursa)
      const r = await creeaza.mutateAsync({ cod: v.cod, denumire: v.denumire, permisiuni: sursa?.permisiuni ?? [] })
      message.success('Rolul a fost creat. Bifează permisiunile și salvează.')
      form.resetFields()
      laInchidere()
      laCreare(r.id)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal title="Rol nou" open={deschis} onCancel={laInchidere} onOk={salveaza} okText="Creează" cancelText="Renunță" confirmLoading={creeaza.isPending} destroyOnHidden>
      <Form
        form={form}
        layout="vertical"
        requiredMark="optional"
        onValuesChange={(schimbat: { denumire?: string }) => {
          // Codul se propune din denumire (fără diacritice, cu „_”), până când utilizatorul îl modifică.
          if (schimbat.denumire !== undefined && !form.isFieldTouched('cod')) {
            form.setFieldValue(
              'cod',
              schimbat.denumire
                .normalize('NFD')
                .replace(/[̀-ͯ]/g, '')
                .toLowerCase()
                .replace(/[^a-z0-9]+/g, '_')
                .replace(/^_|_$/g, ''),
            )
          }
        }}
      >
        <Form.Item name="denumire" label="Denumire" rules={[{ required: true, message: 'Denumirea este obligatorie.' }]}>
          <Input placeholder="Ex.: Responsabil calitate" />
        </Form.Item>
        <Form.Item name="cod" label="Cod" rules={[{ required: true, message: 'Codul este obligatoriu.' }, { max: 60 }]} tooltip="Identificator intern, unic în firmă.">
          <Input />
        </Form.Item>
        <Form.Item name="sursa" label="Pornește de la permisiunile rolului">
          <Select allowClear placeholder="Fără permisiuni" options={roluri.data?.map((r) => ({ value: r.id, label: r.denumire }))} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
