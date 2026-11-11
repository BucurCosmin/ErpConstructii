import { KeyOutlined, PlusOutlined, SafetyOutlined, StopOutlined, UndoOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  Avatar,
  Button,
  Card,
  DatePicker,
  Divider,
  Drawer,
  Flex,
  Form,
  Input,
  Popconfirm,
  Select,
  Skeleton,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'
import {
  useActiune,
  useAdaugaAtribuire,
  useModificaUtilizator,
  useReseteazaParola,
  useRevocaAtribuire,
  useRoluri,
  useUtilizator,
} from '../../api/administrare'
import { usePermisiuni, useProfil, useProiecte } from '../../api/interogari'
import type { AtribuireUtilizator, TipUtilizator } from '../../api/tipuri'
import { etichetaProiect, formatData, tipuriUtilizator } from '../../util/format'
import { ParolaTemporara } from './ParolaTemporara'

const stariAtribuire: Record<AtribuireUtilizator['stare'], { eticheta: string; culoare: string }> = {
  activa: { eticheta: 'Activă', culoare: 'green' },
  viitoare: { eticheta: 'Viitoare', culoare: 'blue' },
  incheiata: { eticheta: 'Încheiată', culoare: 'default' },
}

interface Props {
  id: string | null
  laInchidere: () => void
}

export function FisaUtilizator({ id, laInchidere }: Props) {
  const { data, isLoading } = useUtilizator(id)
  const roluri = useRoluri()
  const proiecte = useProiecte()
  const profil = useProfil()
  const permisiuni = usePermisiuni()
  const modifica = useModificaUtilizator()
  const actiune = useActiune()
  const reseteazaParola = useReseteazaParola()
  const adauga = useAdaugaAtribuire()
  const revoca = useRevocaAtribuire()
  const { message } = App.useApp()
  const [formDate] = Form.useForm<{ nume: string; tip: TipUtilizator; expiraLa: Dayjs | null }>()
  const [formRol] = Form.useForm<{ rolId: string; proiectId?: string; valabilDe: Dayjs; valabilPana?: Dayjs }>()
  const [parola, setParola] = useState<string | null>(null)

  const poateEdita = permisiuni.data?.includes('administrare.editare') ?? false
  const poateAnula = permisiuni.data?.includes('administrare.anulare') ?? false
  const u = data?.utilizator
  const contEditabil = !!data?.poateModificaContul && poateEdita
  const esteEu = u?.id === profil.data?.id
  const numeProiect = (pid: string | null) =>
    pid ? (proiecte.data?.find((p) => p.id === pid)?.cod ?? 'proiect') : 'Toată firma'

  useEffect(() => {
    if (u) formDate.setFieldsValue({ nume: u.nume, tip: u.tip, expiraLa: u.expiraLa ? dayjs(u.expiraLa) : null })
  }, [u, formDate])

  const executa = async (fn: () => Promise<unknown>, succes: string) => {
    try {
      await fn()
      message.success(succes)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const motivBlocareCont = !data?.poateModificaContul
    ? `Utilizatorul are acces și la alte ${data?.alteFirme ?? ''} firme; contul lui îl poate modifica doar administratorul de sistem.`
    : !poateEdita
      ? 'Nu ai drept de editare în administrare.'
      : undefined

  return (
    <Drawer open={!!id} onClose={laInchidere} size={760} title="Fișa utilizatorului" destroyOnHidden>
      {isLoading || !data || !u ? (
        <Skeleton active avatar />
      ) : (
        <>
          <Flex gap={16} align="center" style={{ marginBottom: 20 }}>
            <Avatar size={56} style={{ background: u.areAcces ? '#1f5fbf' : '#bfbfbf', fontSize: 22 }}>
              {u.nume
                .split(' ')
                .map((p) => p[0])
                .join('')
                .slice(0, 2)}
            </Avatar>
            <div>
              <Typography.Title level={4} style={{ margin: 0 }}>
                {u.nume} {esteEu && <Tag>tu</Tag>}
              </Typography.Title>
              <Typography.Text type="secondary">{u.email}</Typography.Text>
              <Flex gap={4} style={{ marginTop: 6 }} wrap>
                <Tag>{tipuriUtilizator[u.tip].eticheta}</Tag>
                {!u.activ ? <Tag color="red">Dezactivat</Tag> : u.areAcces ? <Tag color="green">Activ</Tag> : <Tag>Fără acces</Tag>}
                {u.mfaActivat ? (
                  <Tag icon={<SafetyOutlined />} color="blue">
                    MFA activ
                  </Tag>
                ) : (
                  <Tag>MFA inactiv</Tag>
                )}
                {u.blocat && <Tag color="orange">Blocat temporar</Tag>}
                {u.trebuieSchimbataParola && <Tag color="gold">Parolă temporară</Tag>}
                {data.alteFirme > 0 && <Tag color="purple">Acces și la alte {data.alteFirme} firme</Tag>}
              </Flex>
            </div>
          </Flex>

          <Card size="small" title="Roluri în această firmă" style={{ marginBottom: 16 }}>
            <Table<AtribuireUtilizator>
              rowKey="id"
              size="small"
              pagination={false}
              dataSource={data.atribuiri}
              columns={[
                { title: 'Rol', dataIndex: 'rol', render: (r) => <Typography.Text strong>{r}</Typography.Text> },
                { title: 'Domeniu', dataIndex: 'proiectId', render: numeProiect },
                {
                  title: 'Valabilitate',
                  render: (_, a) => `${formatData(a.valabilDe)} – ${a.valabilPana ? formatData(a.valabilPana) : 'nelimitat'}`,
                },
                {
                  title: 'Stare',
                  dataIndex: 'stare',
                  width: 100,
                  render: (s: AtribuireUtilizator['stare']) => <Tag color={stariAtribuire[s].culoare}>{stariAtribuire[s].eticheta}</Tag>,
                },
                {
                  width: 90,
                  render: (_, a) =>
                    a.stare !== 'incheiata' && (
                      <Popconfirm
                        title="Retragi acest rol?"
                        description="Rolul încetează azi; atribuirea rămâne în istoric."
                        okText="Retrage"
                        cancelText="Renunță"
                        onConfirm={() => executa(() => revoca.mutateAsync(a.id), 'Rolul a fost retras.')}
                        disabled={!poateEdita}
                      >
                        <Button size="small" danger type="link" disabled={!poateEdita}>
                          Retrage
                        </Button>
                      </Popconfirm>
                    ),
                },
              ]}
            />

            {poateEdita && (
              <>
                <Divider style={{ marginBlock: 12 }} />
                <Form
                  form={formRol}
                  layout="inline"
                  initialValues={{ valabilDe: dayjs() }}
                  style={{ rowGap: 8 }}
                  onFinish={(v) =>
                    executa(async () => {
                      await adauga.mutateAsync({
                        id: u.id,
                        rolId: v.rolId,
                        proiectId: v.proiectId ?? null,
                        valabilDe: v.valabilDe.format('YYYY-MM-DD'),
                        valabilPana: v.valabilPana?.format('YYYY-MM-DD') ?? null,
                      })
                      formRol.resetFields()
                    }, 'Rolul a fost adăugat.')
                  }
                >
                  <Form.Item name="rolId" rules={[{ required: true, message: 'Alege rolul' }]}>
                    <Select
                      placeholder="Rol"
                      style={{ width: 200 }}
                      showSearch={{ optionFilterProp: 'label' }}
                      options={roluri.data?.map((r) => ({ value: r.id, label: r.denumire }))}
                    />
                  </Form.Item>
                  <Form.Item name="proiectId">
                    <Select
                      placeholder="Toată firma"
                      allowClear
                      style={{ width: 170 }}
                      showSearch={{ optionFilterProp: 'label' }}
                      options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))}
                      popupMatchSelectWidth={360}
                    />
                  </Form.Item>
                  <Form.Item name="valabilDe" rules={[{ required: true }]}>
                    <DatePicker format="DD.MM.YYYY" placeholder="De la" style={{ width: 130 }} />
                  </Form.Item>
                  <Form.Item name="valabilPana">
                    <DatePicker format="DD.MM.YYYY" placeholder="Până la" style={{ width: 130 }} />
                  </Form.Item>
                  <Button htmlType="submit" icon={<PlusOutlined />} loading={adauga.isPending}>
                    Adaugă
                  </Button>
                </Form>
              </>
            )}
          </Card>

          <Card size="small" title="Datele contului" style={{ marginBottom: 16 }}>
            {motivBlocareCont && <Alert type="info" showIcon title={motivBlocareCont} style={{ marginBottom: 12 }} />}
            <Form
              form={formDate}
              layout="vertical"
              disabled={!contEditabil}
              onFinish={(v) =>
                executa(
                  () => modifica.mutateAsync({ id: u.id, nume: v.nume, tip: v.tip, expiraLa: v.expiraLa?.format('YYYY-MM-DD') ?? null }),
                  'Datele au fost salvate.',
                )
              }
            >
              <Flex gap={12} wrap>
                <Form.Item name="nume" label="Nume" rules={[{ required: true }]} style={{ flex: 2, minWidth: 200 }}>
                  <Input />
                </Form.Item>
                <Form.Item name="tip" label="Tip" style={{ flex: 2, minWidth: 200 }}>
                  <Select
                    options={(Object.keys(tipuriUtilizator) as TipUtilizator[])
                      .filter((t) => t !== 'AdministratorSistem' || profil.data?.tip === 'AdministratorSistem')
                      .map((t) => ({ value: t, label: tipuriUtilizator[t].eticheta }))}
                  />
                </Form.Item>
                <Form.Item name="expiraLa" label="Contul expiră la" tooltip="Pentru auditori și colaboratori temporari." style={{ flex: 1, minWidth: 150 }}>
                  <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
                </Form.Item>
              </Flex>
              <Button type="primary" htmlType="submit" loading={modifica.isPending}>
                Salvează
              </Button>
            </Form>
          </Card>

          <Card size="small" title="Securitate">
            <Space wrap>
              <Tooltip title={motivBlocareCont}>
                <Popconfirm
                  title="Resetezi parola?"
                  description="Se generează o parolă temporară; sesiunile deschise ale utilizatorului se închid."
                  okText="Resetează"
                  cancelText="Renunță"
                  disabled={!contEditabil}
                  onConfirm={() =>
                    executa(async () => setParola((await reseteazaParola.mutateAsync(u.id)).parolaTemporara), 'Parola a fost resetată.')
                  }
                >
                  <Button icon={<KeyOutlined />} disabled={!contEditabil}>
                    Resetează parola
                  </Button>
                </Popconfirm>
              </Tooltip>
              <Tooltip title={motivBlocareCont ?? (!u.mfaActivat ? 'Verificarea în doi pași nu este activă.' : undefined)}>
                <Popconfirm
                  title="Resetezi verificarea în doi pași?"
                  description="Pentru telefon pierdut. La următorul login, utilizatorul o configurează din nou."
                  okText="Resetează"
                  cancelText="Renunță"
                  disabled={!contEditabil || !u.mfaActivat}
                  onConfirm={() => executa(() => actiune.mutateAsync({ id: u.id, actiune: 'resetare-mfa' }), 'Verificarea în doi pași a fost resetată.')}
                >
                  <Button icon={<UndoOutlined />} disabled={!contEditabil || !u.mfaActivat}>
                    Resetează MFA
                  </Button>
                </Popconfirm>
              </Tooltip>
              {u.activ ? (
                <Tooltip title={esteEu ? 'Nu îți poți dezactiva propriul cont.' : motivBlocareCont}>
                  <Popconfirm
                    title="Dezactivezi contul?"
                    description="Utilizatorul nu se mai poate autentifica; sesiunile deschise se închid."
                    okText="Dezactivează"
                    okButtonProps={{ danger: true }}
                    cancelText="Renunță"
                    disabled={!contEditabil || !poateAnula || esteEu}
                    onConfirm={() => executa(() => actiune.mutateAsync({ id: u.id, actiune: 'dezactivare' }), 'Contul a fost dezactivat.')}
                  >
                    <Button danger icon={<StopOutlined />} disabled={!contEditabil || !poateAnula || esteEu}>
                      Dezactivează contul
                    </Button>
                  </Popconfirm>
                </Tooltip>
              ) : (
                <Button
                  disabled={!contEditabil}
                  onClick={() => executa(() => actiune.mutateAsync({ id: u.id, actiune: 'activare' }), 'Contul a fost reactivat.')}
                >
                  Reactivează contul
                </Button>
              )}
            </Space>
          </Card>
        </>
      )}

      <ParolaTemporara parola={parola} pentru={u?.nume ?? ''} laInchidere={() => setParola(null)} />
    </Drawer>
  )
}
