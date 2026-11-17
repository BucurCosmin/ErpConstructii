import { DeleteOutlined, EditOutlined, PlusOutlined, SwapOutlined, UploadOutlined } from '@ant-design/icons'
import { App, Button, Card, Empty, Form, Input, Modal, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useCreeazaObiectiv, useModificaObiectiv, useMutaDeviz, useObiective, useStergeDeviz, useStergeObiectiv } from '../../api/devize'
import type { Obiectiv } from '../../api/devize'
import { useDevize } from '../../api/interogari'
import type { VersiuneDeviz } from '../../api/tipuri'
import { stariVersiune, tipuriVersiune } from '../../util/format'

type Dialog =
  | { tip: 'obiectiv'; obiectiv?: Obiectiv }
  | { tip: 'sterge-obiectiv'; obiectiv: Obiectiv; cuAprobate: boolean }
  | { tip: 'sterge-versiune'; versiune: VersiuneDeviz }
  | { tip: 'muta'; versiune: VersiuneDeviz }

/**
 * Devizele proiectului pe obiective (D-59): fiecare obiectiv cu istoria devizului lui (versiunile, cea de referință marcată),
 * obiectivele noi / redenumite / șterse (importul greșit), versiunile șterse (ciorna oricând; cea aprobată dacă e ultima, cu
 * motiv) sau mutate la alt obiectiv.
 */
export function ObiectiveDevize({ proiectId, poateImporta, poateEdita, laImport }: {
  proiectId: string
  poateImporta: boolean
  poateEdita: boolean
  laImport: () => void
}) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const obiective = useObiective(proiectId)
  const devize = useDevize(proiectId)
  const creeaza = useCreeazaObiectiv(proiectId)
  const modifica = useModificaObiectiv(proiectId)
  const stergeObiectiv = useStergeObiectiv(proiectId)
  const stergeVersiune = useStergeDeviz(proiectId)
  const muta = useMutaDeviz(proiectId)
  const [dialog, setDialog] = useState<Dialog>()
  const [form] = Form.useForm<{ denumire?: string; descriere?: string; motiv?: string; obiectivId?: string }>()

  const lista = obiective.data ?? []
  const versiuni = devize.data ?? []
  const eroare = (e: unknown) => message.error((e as Error).message)
  const deschide = (d: Dialog) => {
    form.resetFields()
    if (d.tip === 'obiectiv' && d.obiectiv) form.setFieldsValue({ denumire: d.obiectiv.denumire, descriere: d.obiectiv.descriere ?? undefined })
    setDialog(d)
  }

  const confirma = async () => {
    if (!dialog) return
    const v = await form.validateFields()
    try {
      switch (dialog.tip) {
        case 'obiectiv':
          if (dialog.obiectiv) await modifica.mutateAsync({ id: dialog.obiectiv.id, denumire: v.denumire!, descriere: v.descriere })
          else await creeaza.mutateAsync({ denumire: v.denumire!, descriere: v.descriere })
          message.success(dialog.obiectiv ? 'Obiectivul a fost redenumit.' : 'Obiectivul a fost adăugat: importă-i devizul.')
          break
        case 'sterge-obiectiv':
          await stergeObiectiv.mutateAsync({ id: dialog.obiectiv.id, motiv: v.motiv })
          message.success(`Obiectivul „${dialog.obiectiv.denumire}” a fost șters, cu devizele lui.`)
          break
        case 'sterge-versiune':
          await stergeVersiune.mutateAsync({ versiuneId: dialog.versiune.id, motiv: v.motiv })
          message.success('Versiunea a fost ștearsă.')
          break
        case 'muta':
          await muta.mutateAsync({ versiuneId: dialog.versiune.id, obiectivId: v.obiectivId!, motiv: v.motiv })
          message.success('Devizul (cu istoria lui) a fost mutat.')
          break
      }
      setDialog(undefined)
    } catch (e) {
      eroare(e)
    }
  }


  const coloane = (o: Obiectiv) => [
    { title: 'Tip', dataIndex: 'tip', width: 160, render: (t: VersiuneDeviz['tip']) => tipuriVersiune[t] },
    { title: 'Nr.', dataIndex: 'numar', width: 60 },
    {
      title: 'Denumire', dataIndex: 'denumire',
      render: (d: string, v: VersiuneDeviz) => (
        <>{d}{o.referinta?.versiuneId === v.id && <Tooltip title="Versiunea după care se urmăresc bugetul și consumurile obiectivului"><Tag color="blue" style={{ marginInlineStart: 8 }}>de referință</Tag></Tooltip>}</>
      ),
    },
    {
      title: 'Sursă', dataIndex: 'sursa', width: 190,
      render: (s: string, v: VersiuneDeviz) => {
        const din = v.versiuneAnterioaraId && versiuni.find((x) => x.id === v.versiuneAnterioaraId)
        return v.versiuneAnterioaraId ? `modificare din nr. ${din ? din.numar : '?'}` : s.startsWith('import:') ? `import ${s.slice(7).toUpperCase()}` : s
      },
    },
    {
      title: 'Stare', dataIndex: 'stare', width: 120,
      render: (s: VersiuneDeviz['stare']) => <Tag color={stariVersiune[s].culoare}>{stariVersiune[s].eticheta}</Tag>,
    },
    ...(poateEdita ? [{
      key: 'actiuni', width: 90,
      render: (_: unknown, v: VersiuneDeviz) => (
        <Space size={0} onClick={(e) => e.stopPropagation()}>
          {lista.length > 1 && (
            <Tooltip title="Mută la alt obiectiv (cu toată istoria)"><Button size="small" type="text" icon={<SwapOutlined />} onClick={() => deschide({ tip: 'muta', versiune: v })} /></Tooltip>
          )}
          <Tooltip title={v.stare === 'Ciorna' ? 'Șterge ciorna' : v.stare === 'Inlocuita' ? 'Șterge versiunea din istoric' : 'Șterge versiunea (ex. import greșit); cea pe care o înlocuise revine în vigoare'}>
            <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => deschide({ tip: 'sterge-versiune', versiune: v })} />
          </Tooltip>
        </Space>
      ),
    }] : []),
  ]

  const faraObiectiv = versiuni.filter((v) => !lista.some((o) => o.id === v.obiectivId))

  if (!obiective.isLoading && lista.length === 0 && versiuni.length === 0)
    return (
      <Empty description="Proiectul nu are încă devize">
        <Space>
          {poateImporta && <Button type="primary" icon={<UploadOutlined />} onClick={laImport}>Importă primul deviz</Button>}
          {poateEdita && <Button icon={<PlusOutlined />} onClick={() => deschide({ tip: 'obiectiv' })}>Obiectiv nou</Button>}
        </Space>
        {dialogul()}
      </Empty>
    )

  function dialogul() {
    if (!dialog) return null
    const titlu = {
      'obiectiv': dialog.tip === 'obiectiv' && dialog.obiectiv ? 'Redenumește obiectivul' : 'Obiectiv nou',
      'sterge-obiectiv': 'Șterge obiectivul cu devizele lui',
      'sterge-versiune': 'Șterge versiunea de deviz',
      'muta': 'Mută devizul la alt obiectiv',
    }[dialog.tip]
    const cereMotiv = dialog.tip === 'muta' || (dialog.tip === 'sterge-obiectiv' && dialog.cuAprobate)
      || (dialog.tip === 'sterge-versiune' && stergereCuMotiv(dialog.versiune, versiuni))
    return (
      <Modal open title={titlu} onCancel={() => setDialog(undefined)} onOk={() => void confirma()} destroyOnHidden
        okText={dialog.tip.startsWith('sterge') ? 'Șterge' : 'Salvează'} okButtonProps={{ danger: dialog.tip.startsWith('sterge') }} cancelText="Renunță"
        confirmLoading={creeaza.isPending || modifica.isPending || stergeObiectiv.isPending || stergeVersiune.isPending || muta.isPending}>
        <Form form={form} layout="vertical">
          {dialog.tip === 'obiectiv' && (
            <>
              <Form.Item name="denumire" label="Denumirea obiectivului" rules={[{ required: true, message: 'Scrie denumirea.' }]}>
                <Input maxLength={200} placeholder="ex. Package A, Hala C1, Drum de acces" />
              </Form.Item>
              <Form.Item name="descriere" label="Descriere (opțional)"><Input.TextArea maxLength={1000} rows={2} /></Form.Item>
            </>
          )}
          {dialog.tip === 'sterge-obiectiv' && (
            <Typography.Paragraph>
              Se șterg obiectivul „{dialog.obiectiv.denumire}” și toate cele {dialog.obiectiv.versiuni} versiuni ale devizului lui, cu fișierele originale.
              Ștergerea e refuzată dacă articolele au documente (ex. bonuri de consum).
            </Typography.Paragraph>
          )}
          {dialog.tip === 'sterge-versiune' && (
            <Typography.Paragraph>
              {tipuriVersiune[dialog.versiune.tip]} nr. {dialog.versiune.numar} — {dialog.versiune.denumire}.{' '}
              {descriereStergere(dialog.versiune, versiuni)}
            </Typography.Paragraph>
          )}
          {dialog.tip === 'muta' && (
            <Form.Item name="obiectivId" label="Obiectivul" rules={[{ required: true, message: 'Alege obiectivul.' }]}>
              <Select options={lista.filter((o) => o.id !== dialog.versiune.obiectivId).map((o) => ({ value: o.id, label: o.denumire }))} />
            </Form.Item>
          )}
          {cereMotiv && (
            <Form.Item name="motiv" label="Motivul (rămâne în istoric)" rules={[{ required: dialog.tip !== 'muta', message: 'Scrie motivul.' }]}>
              <Input.TextArea rows={2} maxLength={1000} placeholder="ex. fișier greșit importat" />
            </Form.Item>
          )}
        </Form>
      </Modal>
    )
  }

  return (
    <Space orientation="vertical" size={12} style={{ width: '100%' }}>
      <Space wrap>
        {poateImporta && <Button type="primary" icon={<UploadOutlined />} onClick={laImport}>Importă deviz</Button>}
        {poateEdita && <Button icon={<PlusOutlined />} onClick={() => deschide({ tip: 'obiectiv' })}>Obiectiv nou</Button>}
        <Typography.Text type="secondary">
          Fiecare obiectiv are devizul lui, cu toate versiunile; bugetul se vede pe obiectiv sau pe tot proiectul.
        </Typography.Text>
      </Space>
      {lista.map((o) => {
        const ale = versiuni.filter((v) => v.obiectivId === o.id).sort((a, b) => b.numar - a.numar)
        return (
          <Card key={o.id} size="small"
            title={<Space>{o.denumire}<Typography.Text type="secondary" style={{ fontWeight: 400 }}>{o.descriere}</Typography.Text></Space>}
            extra={poateEdita && (
              <Space size={0}>
                <Tooltip title="Redenumește"><Button size="small" type="text" icon={<EditOutlined />} onClick={() => deschide({ tip: 'obiectiv', obiectiv: o })} /></Tooltip>
                <Tooltip title="Șterge obiectivul cu toate devizele lui (import greșit)">
                  <Button size="small" type="text" danger icon={<DeleteOutlined />}
                    onClick={() => deschide({ tip: 'sterge-obiectiv', obiectiv: o, cuAprobate: ale.some((v) => v.stare !== 'Ciorna') })} />
                </Tooltip>
              </Space>
            )}
            styles={{ body: { padding: 0 } }}>
            <Table<VersiuneDeviz> className="tabel-compact" size="small" rowKey="id" dataSource={ale} pagination={false} columns={coloane(o)}
              locale={{ emptyText: 'Fără deviz: importă-l alegând acest obiectiv.' }}
              onRow={(v) => ({ onClick: () => navigheaza(`/proiecte/${proiectId}/devize/${v.id}`), style: { cursor: 'pointer' } })} />
          </Card>
        )
      })}
      {faraObiectiv.length > 0 && (
        <Card size="small" title="Fără obiectiv" styles={{ body: { padding: 0 } }}>
          <Table<VersiuneDeviz> className="tabel-compact" size="small" rowKey="id" dataSource={faraObiectiv} pagination={false}
            columns={coloane({ id: '', denumire: '', descriere: null, ordine: 0, versiuni: 0, referinta: null })}
            onRow={(v) => ({ onClick: () => navigheaza(`/proiecte/${proiectId}/devize/${v.id}`), style: { cursor: 'pointer' } })} />
        </Card>
      )}
      {dialogul()}
    </Space>
  )
}

/** Ștergerea cere motiv (și dreptul de aprobare) pentru versiunile blocate sau din care au pornit alte versiuni blocate. */
export const stergereCuMotiv = (v: VersiuneDeviz, toate: VersiuneDeviz[]) =>
  v.stare !== 'Ciorna' || toate.some((x) => x.versiuneAnterioaraId === v.id && x.stare !== 'Ciorna')

/** Ce se întâmplă la ștergerea versiunii (D-59), pentru dialogul de confirmare. */
export function descriereStergere(v: VersiuneDeviz, toate: VersiuneDeviz[]) {
  const ciorne = toate.filter((x) => x.versiuneAnterioaraId === v.id && x.stare === 'Ciorna')
  const parti = [
    v.stare === 'Ciorna'
      ? 'Ciorna se șterge cu fișierele ei originale.'
      : v.stare === 'Inlocuita'
        ? 'Versiunea din istoric se șterge, cu fișierele ei; versiunea mai nouă rămâne în vigoare, iar istoria se leagă peste cea ștearsă.'
        : 'Versiunea în vigoare se șterge (ex. import greșit), cu fișierele ei: dacă înlocuise o versiune mai veche, aceea revine în vigoare.',
  ]
  if (ciorne.length > 0) parti.push(`Se șterge și ciorna de lucru pornită din ea (nr. ${ciorne.map((c) => c.numar).join(', ')}).`)
  parti.push('Ștergerea e refuzată dacă articolele ei au documente (bonuri de consum).')
  return parti.join(' ')
}
