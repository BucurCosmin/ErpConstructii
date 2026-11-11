import { DeleteOutlined, FilePdfOutlined, FileWordOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, App, AutoComplete, Button, Card, Col, DatePicker, Form, Input, Radio, Result, Row, Select, Space, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import { useComandaLaFurnizor, useEmiteNotaComanda, useModificaNotaComanda, useNecesareMateriale } from '../../api/aprovizionare'
import type { ConditiiComanda, NotaComanda } from '../../api/aprovizionare'
import { useSabloaneFormular } from '../../api/formulare'
import { useProiecte } from '../../api/interogari'
import { useFurnizori } from '../../api/parteneri'
import type { Adresa } from '../../api/tipuri'
import { NumarRo } from '../../componente/NumarRo'
import { deschidePdf } from '../../util/pdf'
import { etichetaProiect } from '../../util/format'

interface LinieValori {
  denumire: string
  unitateMasura: string
  cantitate: number
  termen?: string
  /** Poziția pe nota acceptată și cantitatea rămasă de comandat (doar în modul „din nota acceptată”). */
  index?: number
  max?: number
  observatii?: string | null
}

interface Valori {
  proiectId: string
  numarExtern?: string
  motiv?: string
  data: Dayjs
  lucrare?: string
  necesarId?: string
  furnizorDenumire: string
  furnizorCui?: string
  furnizorAdresa?: string
  furnizorTelefon?: string
  furnizorEmail?: string
  receptie: string
  clasaCalitate: string
  transport: string
  plata: string
  linii: LinieValori[]
  observatii?: string
}

const adresa = (a: Adresa) => [a.strada, a.codPostal, a.localitate, a.judet].filter((x) => x && x.trim()).join(', ')
const gol = (s?: string | null) => (s && s.trim() ? s.trim() : null)

/** Butoane radio cu varianta „nebifat”, ca pe formularul de hârtie (căsuțe goale). */
function Bife({ nume, eticheta, optiuni }: { nume: keyof Valori; eticheta: string; optiuni: { value: string; label: string }[] }) {
  return (
    <Form.Item name={nume} label={eticheta} style={{ marginBottom: 8 }}>
      <Radio.Group optionType="button" size="small" options={[...optiuni, { value: '', label: '—' }]} />
    </Form.Item>
  )
}

/**
 * Notă de comandă nouă către furnizor (formularul GC 007): proiect / lucrare, furnizor (din nomenclator sau nou),
 * condițiile bifate, materialele (preluate opțional dintr-un necesar aprobat) cu termen. După emitere: Word din șablonul firmei și PDF.
 * Cu `dinNota` (D-37, „Necesar de achiziție”): materialele notei acceptate; ce se șterge sau se micșorează rămâne pe ea
 * pentru alt furnizor. Cu `deModificat` (D-44): modificarea notei emise, cu motiv (numărul și proiectul rămân).
 */
export function NotaComandaNoua({ poateCrea, onEmisa, dinNota, deModificat, onModificat }: {
  poateCrea: boolean
  onEmisa?: () => void
  dinNota?: NotaComanda
  deModificat?: NotaComanda
  onModificat?: (nota: NotaComanda) => void
}) {
  const { message } = App.useApp()
  const navigheaza = useNavigate()
  const [form] = Form.useForm<Valori>()
  const proiecte = useProiecte()
  const furnizori = useFurnizori(false)
  const sabloane = useSabloaneFormular()
  const emite = useEmiteNotaComanda()
  const comanda = useComandaLaFurnizor()
  const modifica = useModificaNotaComanda()
  const [ramase, setRamase] = useState(0)
  const proiectId = Form.useWatch('proiectId', form)
  const necesare = useNecesareMateriale({ proiectId, stare: 'Aprobata' })
  const [emisa, setEmisa] = useState<NotaComanda>()
  const areSablon = sabloane.data?.some((s) => s.codTip === 'nota-comanda' && s.activ) ?? false

  const optiuniFurnizori = useMemo(
    () => (furnizori.data ?? []).map((f) => ({ value: f.denumire, label: f.cui ? `${f.denumire} (${f.cui})` : f.denumire, furnizor: f })),
    [furnizori.data],
  )

  const alegeProiect = (id: string) => {
    const p = proiecte.data?.find((x) => x.id === id)
    form.setFieldsValue({ lucrare: p?.denumire, necesarId: undefined })
  }

  const alegeFurnizor = (denumire: string) => {
    const f = furnizori.data?.find((x) => x.denumire === denumire)
    if (!f) return
    form.setFieldsValue({ furnizorCui: f.cui ?? undefined, furnizorAdresa: adresa(f.adresa) || undefined, furnizorTelefon: f.telefon ?? undefined, furnizorEmail: f.email ?? undefined })
  }

  const alegeNecesar = (id?: string) => {
    const n = necesare.data?.find((x) => x.id === id)
    if (!n) return
    form.setFieldsValue({ linii: n.linii.map((l) => ({ denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate })) })
    message.info(`Am preluat ${n.linii.length} materiale din necesarul ${n.numar}. Șterge ce nu comanzi la acest furnizor.`)
  }

  const conditiiDin = (v: Valori): ConditiiComanda => ({
    receptie: (v.receptie || null) as ConditiiComanda['receptie'],
    clasaCalitate: (v.clasaCalitate || null) as ConditiiComanda['clasaCalitate'],
    transport: (v.transport || null) as ConditiiComanda['transport'],
    plata: (v.plata || null) as ConditiiComanda['plata'],
  })

  const trimite = async (v: Valori) => {
    if (deModificat) {
      try {
        const nota = await modifica.mutateAsync({
          proiectId: deModificat.proiectId,
          id: deModificat.id,
          date: {
            numarExtern: gol(v.numarExtern),
            data: v.data.format('YYYY-MM-DD'),
            lucrare: gol(v.lucrare),
            furnizorDenumire: v.furnizorDenumire.trim(),
            furnizorCui: gol(v.furnizorCui),
            furnizorAdresa: gol(v.furnizorAdresa),
            furnizorTelefon: gol(v.furnizorTelefon),
            furnizorEmail: gol(v.furnizorEmail),
            conditii: conditiiDin(v),
            linii: v.linii.map((l) => ({ denumire: l.denumire.trim(), unitateMasura: l.unitateMasura.trim(), cantitate: l.cantitate, termen: gol(l.termen) })),
            observatii: gol(v.observatii),
            motiv: (v.motiv ?? '').trim(),
          },
        })
        message.success(`${nota.numar} a fost modificată; modificarea apare în istoricul notei.`)
        onModificat?.(nota)
      } catch (e) {
        message.error((e as Error).message)
      }
      return
    }
    if (dinNota) {
      try {
        const r = await comanda.mutateAsync({
          proiectId: dinNota.proiectId,
          id: dinNota.id,
          date: {
            data: v.data.format('YYYY-MM-DD'),
            lucrare: gol(v.lucrare),
            furnizorDenumire: v.furnizorDenumire.trim(),
            furnizorCui: gol(v.furnizorCui),
            furnizorAdresa: gol(v.furnizorAdresa),
            furnizorTelefon: gol(v.furnizorTelefon),
            furnizorEmail: gol(v.furnizorEmail),
            conditii: conditiiDin(v),
            linii: v.linii.map((l) => ({ index: l.index!, denumire: gol(l.denumire), cantitate: l.cantitate, termen: gol(l.termen) })),
            observatii: gol(v.observatii),
          },
        })
        setRamase(r.liniiRamase)
        setEmisa(r.emisa)
        onEmisa?.()
      } catch (e) {
        message.error((e as Error).message)
      }
      return
    }
    try {
      const nota = await emite.mutateAsync({
        proiectId: v.proiectId,
        date: {
          data: v.data.format('YYYY-MM-DD'),
          lucrare: gol(v.lucrare),
          furnizorDenumire: v.furnizorDenumire.trim(),
          furnizorCui: gol(v.furnizorCui),
          furnizorAdresa: gol(v.furnizorAdresa),
          furnizorTelefon: gol(v.furnizorTelefon),
          furnizorEmail: gol(v.furnizorEmail),
          conditii: conditiiDin(v),
          linii: v.linii.map((l) => ({ denumire: l.denumire.trim(), unitateMasura: l.unitateMasura.trim(), cantitate: l.cantitate, termen: gol(l.termen) })),
          observatii: gol(v.observatii),
          necesarId: v.necesarId ?? null,
        },
      })
      setEmisa(nota)
      onEmisa?.()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const word = async (n: NotaComanda) => {
    try {
      const blob = await api.getBinar(`/api/aprovizionare/proiecte/${n.proiectId}/note-comanda/${n.id}/document.docx`)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${n.numar}_${n.furnizorDenumire}`.replace(/[^a-zA-Z0-9_-]+/g, '_') + '.docx'
      a.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  if (emisa) {
    return (
      <Card>
        <Result
          status="success"
          title={`Nota de comandă ${emisa.numar} a fost emisă`}
          subTitle={`${emisa.furnizorDenumire} · ${emisa.proiectCod} · ${emisa.linii.length} materiale${ramase > 0 ? ` · pe nota ${dinNota?.numar} au rămas ${ramase} materiale pentru alt furnizor` : ''}`}
          extra={[
            <Button key="w" type="primary" icon={<FileWordOutlined />} onClick={() => void word(emisa)} disabled={!areSablon}>Descarcă Word</Button>,
            <Button key="p" icon={<FilePdfOutlined />}
              onClick={() => void deschidePdf(`/api/aprovizionare/proiecte/${emisa.proiectId}/note-comanda/${emisa.id}/pdf`).catch((e: Error) => message.error(e.message))}>
              PDF
            </Button>,
            ...(dinNota ? [] : [<Button key="n" icon={<PlusOutlined />} onClick={() => { setEmisa(undefined); form.resetFields() }}>Altă notă de comandă</Button>]),
          ]}
        />
      </Card>
    )
  }

  return (
    <Card title={dinNota || deModificat ? undefined : 'Notă de comandă nouă către furnizor (GC 007)'} variant={dinNota || deModificat ? 'borderless' : undefined}
      styles={dinNota || deModificat ? { body: { padding: 0 } } : undefined}>
      {deModificat && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }}
          title={`Modifici nota emisă ${deModificat.numar}.`}
          description="Numărul și proiectul rămân. Datele de acum se păstrează în istoricul notei, cu motivul și numele tău; dacă nota a fost deja trimisă furnizorului, trimite-i varianta nouă." />
      )}
      {dinNota && (
        <Alert type="info" showIcon style={{ marginBottom: 16 }}
          title="Comanzi la acest furnizor tot sau doar o parte."
          description="Șterge materialele pe care nu le iei de la acest furnizor sau micșorează cantitatea: restul rămâne în „Necesar de achiziție”, pe aceeași notă, pentru alt furnizor. Dacă comanzi tot, nota își păstrează numărul." />
      )}
      {sabloane.isSuccess && !areSablon && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title="Nu există șablonul Word „Notă de comandă către furnizor (GC 007)”."
          description="Nota se poate emite și tipări în PDF (format standard); pentru documentul Word în forma firmei, încarcă șablonul în Formulare și documente."
          action={<Button size="small" onClick={() => navigheaza('/formulare')}>Formulare și documente</Button>}
        />
      )}
      <Form<Valori>
        form={form}
        layout="vertical"
        name="notaComandaNoua"
        initialValues={{
          data: dayjs(), receptie: 'LaPrimire', clasaCalitate: 'BestComercial', transport: '', plata: '',
          ...(deModificat
            ? {
                proiectId: deModificat.proiectId, lucrare: deModificat.lucrare ?? undefined, data: dayjs(deModificat.data),
                numarExtern: deModificat.numarExtern ?? undefined,
                furnizorDenumire: deModificat.furnizorDenumire, furnizorCui: deModificat.furnizorCui ?? undefined,
                furnizorAdresa: deModificat.furnizorAdresa ?? undefined, furnizorTelefon: deModificat.furnizorTelefon ?? undefined,
                furnizorEmail: deModificat.furnizorEmail ?? undefined,
                receptie: deModificat.conditii.receptie ?? '', clasaCalitate: deModificat.conditii.clasaCalitate ?? '',
                transport: deModificat.conditii.transport ?? '', plata: deModificat.conditii.plata ?? '',
                linii: deModificat.linii.map((l) => ({ denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate, termen: l.termen ?? undefined })),
                observatii: deModificat.observatii ?? undefined,
              }
            : dinNota
            ? {
                proiectId: dinNota.proiectId, lucrare: dinNota.lucrare ?? undefined,
                linii: dinNota.linii.map((l, i) => ({
                  denumire: l.denumire, unitateMasura: l.unitateMasura, cantitate: l.cantitate, termen: l.termen ?? undefined, index: i, max: l.cantitate, observatii: l.observatii,
                })),
              }
            : { linii: [{}] }),
        }}
        onFinish={trimite}
      >
        <Row gutter={12}>
          <Col xs={24} md={9}>
            <Form.Item name="proiectId" label="Proiect" rules={[{ required: true, message: 'Alege proiectul.' }]}>
              <Select showSearch={{ optionFilterProp: 'label' }} placeholder="Proiectul (sau proiectul de stoc)" loading={proiecte.isLoading} onChange={alegeProiect} disabled={!!dinNota || !!deModificat}
                options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
            </Form.Item>
          </Col>
          <Col xs={24} md={9}>
            <Form.Item name="lucrare" label="Obiect / lucrare (cum apare pe comandă)">
              <Input maxLength={500} placeholder="ex. CTRF-HSJV-002, Ctr. 1891" />
            </Form.Item>
          </Col>
          <Col xs={12} md={deModificat ? 3 : 6}>
            <Form.Item name="data" label="Data" rules={[{ required: true }]}>
              <DatePicker format="DD.MM.YYYY" style={{ width: '100%' }} allowClear={false} />
            </Form.Item>
          </Col>
          {deModificat && (
            <Col xs={12} md={3}>
              <Form.Item name="numarExtern" label="Nr. pe hârtie">
                <Input maxLength={50} />
              </Form.Item>
            </Col>
          )}
        </Row>

        <Row gutter={12}>
          <Col xs={24} md={9}>
            <Form.Item name="furnizorDenumire" label="Furnizor" rules={[{ required: true, whitespace: true, message: 'Alege sau scrie furnizorul.' }]}
              extra="Din nomenclator sau unul nou (se adaugă la emitere).">
              <AutoComplete options={optiuniFurnizori} onSelect={alegeFurnizor} maxLength={300} placeholder="Caută furnizorul"
                filterOption={(text, o) => (o?.label ?? '').toString().toUpperCase().includes(text.toUpperCase())} />
            </Form.Item>
          </Col>
          <Col xs={12} md={4}>
            <Form.Item name="furnizorCui" label="CUI">
              <Input maxLength={20} />
            </Form.Item>
          </Col>
          <Col xs={12} md={11}>
            <Form.Item name="furnizorAdresa" label="Adresa furnizorului">
              <Input maxLength={500} />
            </Form.Item>
          </Col>
          <Col xs={12} md={5}>
            <Form.Item name="furnizorTelefon" label="Telefon">
              <Input maxLength={100} />
            </Form.Item>
          </Col>
          <Col xs={12} md={7}>
            <Form.Item name="furnizorEmail" label="E-mail">
              <Input maxLength={200} />
            </Form.Item>
          </Col>
        </Row>

        <Card size="small" title="Condiții (bifele de pe formular)" style={{ marginBottom: 16 }}>
          <Row gutter={24}>
            <Col xs={24} md={12}>
              <Bife nume="receptie" eticheta="Recepție" optiuni={[{ value: 'LaFurnizor', label: 'la furnizor' }, { value: 'LaPrimire', label: 'la primire' }]} />
            </Col>
            <Col xs={24} md={12}>
              <Bife nume="clasaCalitate" eticheta="Clasa de calitate" optiuni={[{ value: 'Clasa4', label: 'clasa 4' }, { value: 'BestComercial', label: 'best comercial' }]} />
            </Col>
            <Col xs={24} md={12}>
              <Bife nume="transport" eticheta="Transport" optiuni={[{ value: 'Auto', label: 'auto' }, { value: 'Cfr', label: 'CFR' }]} />
            </Col>
            <Col xs={24} md={12}>
              <Bife nume="plata" eticheta="Plata" optiuni={[{ value: 'Op', label: 'OP' }, { value: 'FilaCec', label: 'fila CEC' }, { value: 'Alte', label: 'alte' }]} />
            </Col>
          </Row>
        </Card>

        <Card
          size="small"
          title="Materiale / produse"
          extra={deModificat ? (deModificat.necesarNumar && <Typography.Text type="secondary">din necesarul {deModificat.necesarNumar}</Typography.Text>)
            : dinNota ? (dinNota.necesarNumar && <Typography.Text type="secondary">din necesarul {dinNota.necesarNumar}</Typography.Text>) : (
            <Form.Item name="necesarId" noStyle>
              <Select allowClear style={{ width: 300 }} placeholder={proiectId ? 'Preia dintr-un necesar aprobat…' : 'Alege întâi proiectul'} disabled={!proiectId}
                loading={necesare.isLoading} onChange={alegeNecesar}
                options={necesare.data?.map((n) => ({ value: n.id, label: `${n.numar} · ${dayjs(n.data).format('DD.MM.YYYY')} · ${n.linii.length} mat.` }))} />
            </Form.Item>
          )}
          style={{ marginBottom: 16 }}
        >
          <Form.List name="linii" rules={[{ validator: async (_, l) => { if (!l?.length) throw new Error('Adaugă cel puțin un material.') } }]}>
            {(campuri, { add, remove }, { errors }) => (
              <>
                <Row gutter={8} style={{ fontWeight: 600, marginBottom: 4 }}>
                  <Col span={1}>Nr.</Col>
                  <Col span={11}>Material / produs</Col>
                  <Col span={3}>U.M.</Col>
                  <Col span={4}>Cantitate</Col>
                  <Col span={4}>Termen</Col>
                </Row>
                {campuri.map(({ key, name }, i) => {
                  const max = form.getFieldValue(['linii', name, 'max']) as number | undefined
                  const obs = form.getFieldValue(['linii', name, 'observatii']) as string | null | undefined
                  return (
                  <Row gutter={8} key={key} align="top">
                    <Col span={1} style={{ paddingTop: 5 }}>
                      {i + 1}.
                      {/* poziția pe nota acceptată: înregistrată ca să ajungă în valorile trimise */}
                      {dinNota && <Form.Item name={[name, 'index']} hidden noStyle><Input /></Form.Item>}
                    </Col>
                    <Col span={11}>
                      <Form.Item name={[name, 'denumire']} rules={[{ required: true, whitespace: true, message: 'Denumirea' }]} style={{ marginBottom: 6 }}
                        extra={obs ? `Observații din necesar: ${obs}` : undefined}>
                        <Input maxLength={300} />
                      </Form.Item>
                    </Col>
                    <Col span={3}>
                      <Form.Item name={[name, 'unitateMasura']} rules={[{ required: true, whitespace: true, message: 'UM' }]} style={{ marginBottom: 6 }}>
                        <Input maxLength={30} placeholder="BUC" disabled={!!dinNota} />
                      </Form.Item>
                    </Col>
                    <Col span={4}>
                      <Form.Item name={[name, 'cantitate']} style={{ marginBottom: 6 }}
                        rules={[{ required: true, message: 'Cantitatea' }, { type: 'number', min: 0.0001, max, message: max !== undefined ? `Între 0 și ${max.toLocaleString('ro-RO')}` : 'Pozitivă' }]}>
                        <NumarRo min={0} precision={undefined} style={{ width: '100%' }} />
                      </Form.Item>
                    </Col>
                    <Col span={4}>
                      <Form.Item name={[name, 'termen']} style={{ marginBottom: 6 }}>
                        <Input maxLength={100} placeholder="ex. imediat" />
                      </Form.Item>
                    </Col>
                    <Col span={1}>
                      <Button type="text" danger icon={<DeleteOutlined />} onClick={() => remove(name)} disabled={campuri.length === 1}
                        title={dinNota ? 'Nu comand acum la acest furnizor (rămâne pe notă)' : undefined} />
                    </Col>
                  </Row>
                  )
                })}
                <Form.ErrorList errors={errors} />
                {!dinNota && <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})} style={{ marginTop: 4 }}>Adaugă material</Button>}
              </>
            )}
          </Form.List>
        </Card>

        <Form.Item name="observatii" label="Observații (apar pe comandă)">
          <Input.TextArea rows={2} maxLength={1000} />
        </Form.Item>

        {deModificat && (
          <Form.Item name="motiv" label="Motivul modificării" rules={[{ required: true, whitespace: true, message: 'Scrie de ce modifici nota.' }]}>
            <Input.TextArea rows={2} maxLength={1000} placeholder="ex. furnizorul a confirmat altă cantitate" />
          </Form.Item>
        )}

        <Space>
          <Button type="primary" htmlType="submit" loading={emite.isPending || comanda.isPending || modifica.isPending} disabled={!poateCrea}>
            {deModificat ? 'Salvează modificarea' : dinNota ? 'Emite comanda la furnizor' : 'Emite nota de comandă'}
          </Button>
          <Typography.Text type="secondary">
            {deModificat ? 'Modificarea se înregistrează în istoricul notei și în jurnalul de modificări.'
              : dinNota ? 'Restul materialelor rămâne pe notă; comanda emisă se poate modifica ulterior doar cu motiv.'
              : 'Numărul (NC/AAAA/NNNN) se dă la emitere; după aceea nota se modifică doar cu motiv (istoric).'}
          </Typography.Text>
        </Space>
      </Form>
    </Card>
  )
}
