import { SwapOutlined } from '@ant-design/icons'
import { Alert, App, Button, Empty, Form, Input, Modal, Spin, Table, Timeline, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect } from 'react'
import { useCorecteazaNir, useStareCorectieNir } from '../../api/stocuri'
import type { LinieStareCorectieNir, Nir } from '../../api/stocuri'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatLei } from '../../util/format'

interface LinieValori {
  cantitateFactura: number
  cantitateReceptionata: number
  pretUnitar: number | null
  observatii?: string
}

interface Valori {
  linii: LinieValori[]
  motiv: string
}

const bani = (v: number) => Math.round(v * 100) / 100

/**
 * Corectura unui NIR recepționat greșit (D-35): cantitatea cf. factură, cantitatea recepționată, prețul și observațiile.
 * Alături: ce scrie acum pe factură (după corecturile ei), cu „Ca pe factură” într-un clic. Stocul primește diferențele;
 * istoricul corecturilor rămâne.
 */
export function CorectieNir({ nir, onInchide }: { nir?: Nir; onInchide: () => void }) {
  const { message, modal } = App.useApp()
  const stare = useStareCorectieNir(nir?.id)
  const corecteaza = useCorecteazaNir()
  const [form] = Form.useForm<Valori>()
  const valori = Form.useWatch('linii', form) as LinieValori[] | undefined
  const linii = stare.data?.linii ?? []
  const preturi = stare.data?.vedePreturi ?? false

  useEffect(() => {
    if (!stare.data) return
    form.setFieldsValue({
      linii: stare.data.linii.map((l) => ({ cantitateFactura: l.cantitateFactura, cantitateReceptionata: l.cantitateReceptionata, pretUnitar: l.pretUnitar, observatii: l.observatii ?? undefined })),
      motiv: '',
    })
  }, [stare.data, form])

  const caPeFactura = (i: number, l: LinieStareCorectieNir) => {
    if (l.facturaCantitate === null) return
    const v = form.getFieldValue(['linii', i]) as LinieValori
    form.setFieldValue(['linii', i], {
      ...v,
      // Ce s-a recepționat integral rămâne integral și după corectura facturii.
      cantitateReceptionata: v.cantitateReceptionata === l.cantitateFactura ? l.facturaCantitate : v.cantitateReceptionata,
      cantitateFactura: l.facturaCantitate,
      pretUnitar: preturi && l.facturaPret !== null ? l.facturaPret : v.pretUnitar,
    })
  }

  const salveaza = async (v: Valori) => {
    if (!nir) return
    const schimbate = linii.map((l, i) => ({ l, v: v.linii[i] })).filter(({ l, v: x }) =>
      x.cantitateFactura !== l.cantitateFactura || x.cantitateReceptionata !== l.cantitateReceptionata
      || (preturi && x.pretUnitar !== l.pretUnitar) || (x.observatii?.trim() || null) !== (l.observatii ?? null))
    if (schimbate.length === 0) {
      message.info('Nu ai modificat nimic.')
      return
    }
    try {
      const r = await corecteaza.mutateAsync({
        id: nir.id,
        cerere: {
          linii: schimbate.map(({ l, v: x }) => ({
            indexFactura: l.indexFactura, cantitateFactura: Number(x.cantitateFactura), cantitateReceptionata: Number(x.cantitateReceptionata),
            pretUnitar: preturi ? Number(x.pretUnitar ?? 0) : null, observatii: x.observatii?.trim() || null,
          })),
          motiv: v.motiv.trim(),
          data: null,
        },
      })
      message.success(r.mesaj)
      modal.info({ title: 'Ce s-a schimbat (NIR și stoc)', content: r.rezumat, width: 640 })
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal open={!!nir} onCancel={onInchide} width={1250} style={{ top: 24 }} destroyOnHidden
      title={nir && `Corectează ${nir.numar} — factura ${nir.facturaNumar}, ${nir.furnizorDenumire}`}
      okText="Salvează corectura" cancelText="Renunță" onOk={() => form.submit()} confirmLoading={corecteaza.isPending}
      okButtonProps={{ disabled: !stare.data || stare.data.stornat }}>
      {stare.isLoading && <Spin />}
      {stare.error && <Alert type="error" showIcon title={(stare.error as Error).message} />}
      {stare.data?.stornat && <Alert type="warning" showIcon title="NIR-ul e stornat: nu se mai corectează. Fă o recepție nouă." />}
      {stare.data && !stare.data.stornat && (
        <>
          <Alert type="info" showIcon style={{ marginBottom: 12 }}
            title="Stocul depozitului primește diferența față de recepția inițială (plus sau minus)."
            description="Dacă materialul a plecat deja în șantier, minusul se poate scoate doar din ce a rămas în depozit (fă întâi returul). Coloana „Pe factură acum” arată factura după corecturile ei; „Ca pe factură” aduce linia la ea." />
          <Form<Valori> form={form} layout="vertical" onFinish={salveaza} size="small">
            <Table<LinieStareCorectieNir>
              size="small" className="tabel-compact" tableLayout="fixed" pagination={false} rowKey="indexFactura" dataSource={linii}
              columns={[
                { title: 'Material', dataIndex: 'denumire', ellipsis: { showTitle: true } },
                { title: 'UM', dataIndex: 'unitateMasura', width: 55 },
                {
                  title: 'Pe factură acum', width: 175,
                  render: (_, l, i) => {
                    if (l.facturaCantitate === null) return <Typography.Text type="secondary">—</Typography.Text>
                    const v = valori?.[i]
                    const diferit = v && (v.cantitateFactura !== l.facturaCantitate || (preturi && l.facturaPret !== null && v.pretUnitar !== l.facturaPret))
                    return (
                      <span>
                        <Typography.Text type={diferit ? 'warning' : undefined}>
                          {formatCantitate(l.facturaCantitate)}{preturi && l.facturaPret !== null ? ` × ${formatLei(l.facturaPret)}` : ''}
                        </Typography.Text>
                        {diferit && (
                          <Tooltip title="Ca pe factură: cantitatea cf. factură și prețul (și recepționatul, dacă era integral)">
                            <Button size="small" type="link" icon={<SwapOutlined />} onClick={() => caPeFactura(i, l)} />
                          </Tooltip>
                        )}
                      </span>
                    )
                  },
                },
                {
                  title: 'Cf. factură', width: 110,
                  render: (_, __, i) => <Form.Item name={['linii', i, 'cantitateFactura']} noStyle rules={[{ required: true }]}><NumarRo min={0} style={{ width: '100%' }} /></Form.Item>,
                },
                {
                  title: 'Recepționat', width: 110,
                  render: (_, __, i) => <Form.Item name={['linii', i, 'cantitateReceptionata']} noStyle rules={[{ required: true }]}><NumarRo min={0} style={{ width: '100%' }} /></Form.Item>,
                },
                ...(preturi ? [
                  {
                    title: 'Preț unitar', width: 115,
                    render: (_: unknown, __: LinieStareCorectieNir, i: number) => <Form.Item name={['linii', i, 'pretUnitar']} noStyle><NumarRo min={0} style={{ width: '100%' }} /></Form.Item>,
                  },
                  {
                    title: 'Valoare', width: 150, className: 'numeric',
                    render: (_: unknown, l: LinieStareCorectieNir, i: number) => {
                      const v = valori?.[i]
                      const nou = bani((v?.cantitateReceptionata ?? 0) * (v?.pretUnitar ?? 0))
                      const dif = bani(nou - (l.valoare ?? 0))
                      return <>{formatLei(nou)}{dif !== 0 && <div><Typography.Text type={dif < 0 ? 'danger' : 'success'}>{dif > 0 ? '+' : ''}{formatLei(dif)}</Typography.Text></div>}</>
                    },
                  },
                ] : []),
                {
                  title: 'Diferență stoc', width: 105, className: 'numeric',
                  render: (_, l, i) => {
                    const d = (valori?.[i]?.cantitateReceptionata ?? l.cantitateReceptionata) - l.cantitateReceptionata
                    return d === 0 ? '' : <Typography.Text type={d < 0 ? 'danger' : 'success'}>{d > 0 ? '+' : ''}{formatCantitate(d)}</Typography.Text>
                  },
                },
                { title: 'Observații', width: 200, render: (_, __, i) => <Form.Item name={['linii', i, 'observatii']} noStyle><Input maxLength={500} /></Form.Item> },
              ]}
            />
            <Form.Item name="motiv" label="Motivul corecturii" style={{ marginTop: 12 }} rules={[{ required: true, whitespace: true, message: 'Motivul este obligatoriu.' }]}>
              <Input.TextArea rows={2} maxLength={1000} placeholder="ex. cantitatea a fost citită greșit de pe factură (7.000 în loc de 7)" />
            </Form.Item>
          </Form>
          <Typography.Title level={5}>Corecturi anterioare</Typography.Title>
          {stare.data.corectii.length === 0
            ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Niciuna" />
            : (
              <Timeline items={stare.data.corectii.map((c) => ({
                children: (
                  <>
                    <Typography.Text strong>{dayjs(c.corectatLa).format('DD.MM.YYYY HH:mm')} · {c.corectatDe}</Typography.Text> — {c.motiv}
                    <div><Typography.Text type="secondary">{c.rezumat}</Typography.Text></div>
                  </>
                ),
              }))} />
            )}
        </>
      )}
    </Modal>
  )
}
