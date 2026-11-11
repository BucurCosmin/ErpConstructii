import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, AutoComplete, Button, Checkbox, Col, DatePicker, Form, Input, Modal, Row, Segmented, Select, Space, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect } from 'react'
import { tipuriContract, useParteneriContract, useSalveazaContract } from '../../api/contracte'
import type { Contract, TipContract } from '../../api/contracte'
import { useProiecte } from '../../api/interogari'
import { NumarRo } from '../../componente/NumarRo'
import { etichetaProiect, formatLei } from '../../util/format'

const PARTENER_NOU = '__nou__'

interface Linie {
  denumire?: string
  unitateMasura?: string
  cantitate?: number | null
  pretUnitar?: number | null
}

interface Valori {
  proiectId?: string
  partenerId?: string
  partenerDenumire?: string
  partenerCui?: string
  tip: TipContract
  numarExtern?: string
  dataSemnarii: Dayjs
  obiect?: string
  moneda: string
  pret: 'global' | 'pozitii'
  valoare?: number | null
  linii: Linie[]
  perioada?: [Dayjs | null, Dayjs | null] | null
  termenPlataZile?: number | null
  procentGarantie?: number | null
  avans?: number | null
  taxareInversa: boolean
  observatii?: string
}

/**
 * Contract nou (ciornă) sau modificarea unei ciorne: partenerul, obiectul, valoarea (globală sau pe poziții cu prețuri
 * unitare), termenele, plata, garanția de bună execuție, avansul. După semnare („Activează”) nu se mai modifică.
 */
export function EditorContract({ deschis, proiectId, contract, onInchide, onSalvat }: {
  deschis: boolean; proiectId?: string; contract?: Contract; onInchide: () => void; onSalvat?: (c: Contract) => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<Valori>()
  const proiecte = useProiecte()
  const parteneri = useParteneriContract()
  const salveaza = useSalveazaContract()
  const pret = Form.useWatch('pret', form)
  const partenerId = Form.useWatch('partenerId', form)
  const linii = (Form.useWatch('linii', form) as Linie[] | undefined) ?? []
  const totalPozitii = linii.reduce((s, l) => s + Math.round((l?.cantitate ?? 0) * (l?.pretUnitar ?? 0) * 100) / 100, 0)

  useEffect(() => {
    if (!deschis) return
    form.resetFields()
    if (contract) {
      form.setFieldsValue({
        proiectId: contract.proiectId,
        partenerId: contract.partenerId,
        tip: contract.tip,
        numarExtern: contract.numarExtern ?? undefined,
        dataSemnarii: dayjs(contract.dataSemnarii),
        obiect: contract.obiect,
        moneda: contract.moneda,
        pret: contract.linii.length > 0 ? 'pozitii' : 'global',
        valoare: contract.valoare,
        linii: contract.linii.length > 0 ? contract.linii : [{}],
        perioada: [contract.dataInceput ? dayjs(contract.dataInceput) : null, contract.dataSfarsit ? dayjs(contract.dataSfarsit) : null],
        termenPlataZile: contract.termenPlataZile,
        procentGarantie: Math.round(contract.procentGarantie * 10000) / 100,
        avans: contract.avans,
        taxareInversa: contract.taxareInversa,
        observatii: contract.observatii ?? undefined,
      })
    } else {
      form.setFieldsValue({ proiectId, tip: 'Subantreprenoriat', dataSemnarii: dayjs(), moneda: 'RON', pret: 'global', linii: [{}], procentGarantie: 5, avans: 0, taxareInversa: true, termenPlataZile: 30 })
    }
  }, [deschis, contract, proiectId, form])

  const trimite = async (v: Valori) => {
    const pid = contract?.proiectId ?? v.proiectId
    if (!pid) return
    try {
      const c = await salveaza.mutateAsync({
        proiectId: pid,
        id: contract?.id,
        date: {
          numarExtern: v.numarExtern?.trim() || null,
          dataSemnarii: v.dataSemnarii.format('YYYY-MM-DD'),
          partenerId: v.partenerId && v.partenerId !== PARTENER_NOU ? v.partenerId : null,
          partenerDenumire: v.partenerId === PARTENER_NOU ? (v.partenerDenumire ?? '').trim() : null,
          partenerCui: v.partenerId === PARTENER_NOU ? v.partenerCui?.trim() || null : null,
          tip: v.tip,
          obiect: (v.obiect ?? '').trim(),
          moneda: v.moneda,
          valoare: v.pret === 'global' ? Number(v.valoare ?? 0) : 0,
          linii: v.pret === 'pozitii'
            ? v.linii.map((l) => ({ denumire: (l.denumire ?? '').trim(), unitateMasura: (l.unitateMasura ?? '').trim(), cantitate: Number(l.cantitate ?? 0), pretUnitar: Number(l.pretUnitar ?? 0) }))
            : [],
          dataInceput: v.perioada?.[0]?.format('YYYY-MM-DD') ?? null,
          dataSfarsit: v.perioada?.[1]?.format('YYYY-MM-DD') ?? null,
          termenPlataZile: v.termenPlataZile ?? null,
          procentGarantie: Number(v.procentGarantie ?? 0) / 100,
          avans: Number(v.avans ?? 0),
          taxareInversa: v.taxareInversa,
          observatii: v.observatii?.trim() || null,
        },
      })
      message.success(contract ? `Contractul ${c.numar} a fost modificat.` : `Contractul ${c.numar} a fost creat (ciornă). După semnare, apasă „Activează”.`)
      onSalvat?.(c)
      onInchide()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Modal
      open={deschis}
      onCancel={onInchide}
      width="min(1100px, 96vw)"
      title={contract ? `Modifică ciorna ${contract.numar}` : 'Contract nou cu un subantreprenor / partener'}
      okText={contract ? 'Salvează' : 'Creează ciorna'}
      cancelText="Renunță"
      onOk={() => form.submit()}
      confirmLoading={salveaza.isPending}
      destroyOnHidden
    >
      <Form<Valori> form={form} name="contract" layout="vertical" onFinish={trimite} requiredMark="optional">
        <Row gutter={12}>
          {!contract && !proiectId && (
            <Col xs={24}>
              <Form.Item name="proiectId" label="Proiect" rules={[{ required: true, message: 'Alege proiectul.' }]}>
                <Select showSearch={{ optionFilterProp: 'label' }} options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
              </Form.Item>
            </Col>
          )}
          <Col xs={24} md={12}>
            <Form.Item name="partenerId" label="Partener (subantreprenor / prestator)" rules={[{ required: true, message: 'Alege partenerul.' }]}>
              <Select
                showSearch={{ optionFilterProp: 'label' }}
                loading={parteneri.isLoading}
                options={[
                  { value: PARTENER_NOU, label: '➕ Partener nou (nu e în nomenclator)' },
                  ...(parteneri.data ?? []).map((p) => ({ value: p.id, label: `${p.denumire}${p.cui ? ` (${p.cui})` : ''}${p.esteSubantreprenor ? ' · subantreprenor' : ''}` })),
                ]}
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item name="tip" label="Tipul contractului" rules={[{ required: true }]}>
              <Select
                options={Object.entries(tipuriContract).map(([value, label]) => ({ value, label }))}
                onChange={(t: TipContract) => form.setFieldValue('taxareInversa', t === 'Subantreprenoriat')}
              />
            </Form.Item>
          </Col>
          {partenerId === PARTENER_NOU && (
            <>
              <Col xs={24} md={16}>
                <Form.Item name="partenerDenumire" label="Denumirea partenerului" rules={[{ required: true, whitespace: true }]}>
                  <Input maxLength={300} />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item name="partenerCui" label="CUI">
                  <Input maxLength={20} />
                </Form.Item>
              </Col>
            </>
          )}
          <Col xs={24} md={6}>
            <Form.Item name="numarExtern" label="Nr. contract / registratură" extra="Numărul de pe contractul semnat.">
              <Input maxLength={50} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="dataSemnarii" label="Data contractului" rules={[{ required: true }]}>
              <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item name="perioada" label="Perioada de execuție (început – termen)">
              <DatePicker.RangePicker format="DD.MM.YYYY" style={{ width: '100%' }} allowEmpty={[true, true]} />
            </Form.Item>
          </Col>
          <Col xs={24}>
            <Form.Item name="obiect" label="Obiectul contractului" rules={[{ required: true, whitespace: true, message: 'Descrie lucrările / serviciile.' }]}>
              <Input.TextArea rows={2} maxLength={1000} placeholder="ex. Execuție instalații sanitare corp A, conform anexei 1" />
            </Form.Item>
          </Col>
        </Row>

        <Space align="end" wrap style={{ marginBottom: 8 }}>
          <Form.Item name="pret" label="Prețul" style={{ marginBottom: 8 }}>
            <Segmented options={[{ value: 'global', label: 'Valoare globală (preț ferm)' }, { value: 'pozitii', label: 'Pe poziții (prețuri unitare)' }]} />
          </Form.Item>
          {pret !== 'pozitii' && (
            <Form.Item name="valoare" label="Valoarea fără TVA" style={{ marginBottom: 8 }} rules={[{ required: true, message: 'Valoarea?' }]}>
              <NumarRo min={0} style={{ width: 200 }} />
            </Form.Item>
          )}
          <Form.Item name="moneda" label="Moneda" style={{ marginBottom: 8 }}>
            <Select style={{ width: 100 }} options={['RON', 'EUR'].map((m) => ({ value: m }))} />
          </Form.Item>
        </Space>

        {pret === 'pozitii' && (
          <Form.List name="linii">
            {(campuri, { add, remove }) => (
              <div style={{ marginBottom: 12 }}>
                {campuri.map(({ key, name }) => (
                  <Row key={key} gutter={8}>
                    <Col flex="auto">
                      <Form.Item name={[name, 'denumire']} rules={[{ required: true, whitespace: true, message: 'Denumirea?' }]} style={{ marginBottom: 8 }}>
                        <Input placeholder="Poziția (lucrare / serviciu)" maxLength={300} />
                      </Form.Item>
                    </Col>
                    <Col flex="90px">
                      <Form.Item name={[name, 'unitateMasura']} rules={[{ required: true, whitespace: true, message: 'UM?' }]} style={{ marginBottom: 8 }}>
                        <AutoComplete placeholder="UM" options={['mp', 'mc', 'ml', 'buc', 'ora', 'zi', 'luna', 'to', 'km', 'forfet'].map((u) => ({ value: u }))} />
                      </Form.Item>
                    </Col>
                    <Col flex="130px">
                      <Form.Item name={[name, 'cantitate']} rules={[{ required: true, message: '?' }]} style={{ marginBottom: 8 }}>
                        <NumarRo min={0} placeholder="Cantitate" style={{ width: '100%' }} />
                      </Form.Item>
                    </Col>
                    <Col flex="150px">
                      <Form.Item name={[name, 'pretUnitar']} rules={[{ required: true, message: '?' }]} style={{ marginBottom: 8 }}>
                        <NumarRo min={0} placeholder="Preț unitar" style={{ width: '100%' }} />
                      </Form.Item>
                    </Col>
                    <Col flex="32px">
                      <Button type="text" danger icon={<DeleteOutlined />} disabled={campuri.length === 1} onClick={() => remove(name)} />
                    </Col>
                  </Row>
                ))}
                <Space>
                  <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>Adaugă poziție</Button>
                  <Typography.Text strong>Total: {formatLei(totalPozitii)}</Typography.Text>
                </Space>
              </div>
            )}
          </Form.List>
        )}

        <Row gutter={12}>
          <Col xs={12} md={6}>
            <Form.Item name="procentGarantie" label="Garanție de bună execuție (%)" extra="Reținută din fiecare factură.">
              <NumarRo min={0} max={50} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={12} md={6}>
            <Form.Item name="avans" label="Avans">
              <NumarRo min={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={12} md={6}>
            <Form.Item name="termenPlataZile" label="Termen de plată (zile)">
              <NumarRo min={0} max={365} step={1} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={12} md={6}>
            <Form.Item name="taxareInversa" valuePropName="checked" label=" " extra="Lucrări de construcții între plătitori de TVA.">
              <Checkbox>Taxare inversă</Checkbox>
            </Form.Item>
          </Col>
          <Col xs={24}>
            <Form.Item name="observatii" label="Observații (penalități, condiții de recepție…)">
              <Input.TextArea rows={2} maxLength={2000} />
            </Form.Item>
          </Col>
        </Row>
        <Alert type="info" showIcon title="Contractul se creează ca ciornă. După semnare îl activezi: nu se mai modifică decât prin acte adiționale, iar facturile partenerului se leagă de el." />
      </Form>
    </Modal>
  )
}
