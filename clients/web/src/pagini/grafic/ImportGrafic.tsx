import { InboxOutlined } from '@ant-design/icons'
import { Alert, App, Descriptions, Input, Modal, Radio, Space, Spin, Tag, Typography, Upload } from 'antd'
import { useState } from 'react'
import { previzualizeazaGrafic, useImportGrafic } from '../../api/grafic'
import type { Grafic, PrevizualizareGrafic } from '../../api/grafic'
import { formatData, formatLei } from '../../util/format'

/** Decalajul în zile, cu semn: „+12 zile” (întârziere) / „−3 zile”. */
export const zile = (z: number) => `${z > 0 ? '+' : z < 0 ? '−' : ''}${Math.abs(z)} ${Math.abs(z) === 1 ? 'zi' : 'zile'}`

/**
 * Importul graficului din Primavera (XER): fișierul se citește și se arată ce conține și, dacă e o actualizare a unui grafic
 * existent (același Project ID), ce s-a schimbat față de actualizarea curentă. Utilizatorul poate alege să-l importe ca
 * grafic nou (altă planificare pe același proiect).
 */
export function ImportGrafic({ proiectId, grafice, deschis, graficId, laInchidere }: {
  proiectId: string
  grafice: Grafic[]
  deschis: boolean
  /** Graficul pe care utilizatorul vrea să-l actualizeze (butonul „Actualizare”); altfel se caută după Project ID. */
  graficId?: string
  laInchidere: (versiune?: { graficId: string; versiuneId: string }) => void
}) {
  const { message } = App.useApp()
  const importa = useImportGrafic(proiectId)
  const [fisier, setFisier] = useState<File>()
  const [p, setP] = useState<PrevizualizareGrafic>()
  const [citeste, setCiteste] = useState(false)
  const [eroare, setEroare] = useState<string>()
  // „actualizare” = versiune nouă a graficului găsit / ales; „nou” = grafic separat.
  const [mod, setMod] = useState<'actualizare' | 'nou'>('actualizare')
  const [tinta, setTinta] = useState<string | undefined>(graficId)
  const [denumire, setDenumire] = useState('')

  const inchide = (rezultat?: { graficId: string; versiuneId: string }) => {
    setFisier(undefined)
    setP(undefined)
    setEroare(undefined)
    setMod('actualizare')
    setTinta(graficId)
    setDenumire('')
    laInchidere(rezultat)
  }

  const analizeaza = async (f: File, o: { graficId?: string; graficNou?: boolean }) => {
    setCiteste(true)
    setEroare(undefined)
    try {
      const r = await previzualizeazaGrafic(proiectId, f, o)
      setP(r)
      setDenumire(r.denumire)
      setMod(r.graficExistent && !o.graficNou ? 'actualizare' : 'nou')
      if (r.graficExistent) setTinta(r.graficExistent.id)
    } catch (e) {
      setP(undefined)
      setEroare((e as Error).message)
    } finally {
      setCiteste(false)
    }
  }

  const alegeFisier = (f: File) => {
    setFisier(f)
    void analizeaza(f, { graficId: tinta })
    return false
  }

  const schimbaTinta = (m: 'actualizare' | 'nou', id?: string) => {
    setMod(m)
    setTinta(id)
    if (fisier) void analizeaza(fisier, m === 'nou' ? { graficNou: true } : { graficId: id })
  }

  const confirma = async () => {
    if (!fisier || !p) return
    try {
      const v = await importa.mutateAsync({
        fisier,
        graficId: mod === 'actualizare' ? p.graficExistent?.id : undefined,
        graficNou: mod === 'nou',
        denumire: mod === 'nou' ? denumire : undefined,
      })
      message.success(mod === 'nou' ? 'Graficul a fost importat.' : `Actualizarea nr. ${v.numar} a fost importată.`)
      // Graficul nou nu e încă în listă: pagina îl găsește după versiune.
      inchide({ graficId: mod === 'actualizare' ? p.graficExistent!.id : '', versiuneId: v.id })
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  const m = p?.moneda ?? ''
  const c = p?.comparatie
  const erori = p?.probleme.filter((x) => x.severitate === 'Eroare') ?? []
  const avertismente = p?.probleme.filter((x) => x.severitate === 'Avertisment') ?? []
  const altele = grafice.filter((g) => g.id !== p?.graficExistent?.id)

  return (
    <Modal open={deschis} onCancel={() => inchide()} width="min(900px, 96vw)" title="Importă graficul de execuție (Primavera XER)"
      okText={mod === 'nou' ? 'Importă ca grafic nou' : `Importă ca actualizarea nr. ${(p?.graficExistent?.versiuni[0]?.numar ?? 0) + 1}`}
      okButtonProps={{ disabled: !p?.poateFiConfirmat }} cancelText="Renunță" onOk={() => void confirma()} confirmLoading={importa.isPending} destroyOnHidden>
      <Upload.Dragger accept=".xer" multiple={false} showUploadList={false} beforeUpload={alegeFisier} disabled={citeste}>
        <p className="ant-upload-drag-icon"><InboxOutlined /></p>
        <p className="ant-upload-text">{fisier ? fisier.name : 'Trage aici exportul XER sau apasă pentru a-l alege'}</p>
        <p className="ant-upload-hint">Primavera P6: File → Export → Primavera PM – (XER). Un export nou al aceleiași planificări devine o actualizare a graficului.</p>
      </Upload.Dragger>

      {citeste && <div style={{ textAlign: 'center', padding: 24 }}><Spin tip="Citesc graficul…"><div style={{ height: 40 }} /></Spin></div>}
      {eroare && <Alert type="error" showIcon title={eroare} style={{ marginTop: 12 }} />}

      {p && !citeste && (
        <Space orientation="vertical" size={12} style={{ width: '100%', marginTop: 12 }}>
          {erori.map((x, i) => <Alert key={i} type="error" showIcon title={x.mesaj} />)}
          {avertismente.length > 0 && (
            <Alert type="warning" showIcon title={`${avertismente.length} avertismente`}
              description={<ul style={{ margin: 0, paddingLeft: 18, maxHeight: 120, overflow: 'auto' }}>{avertismente.map((x, i) => <li key={i}>{x.locatie}: {x.mesaj}</li>)}</ul>} />
          )}

          <Descriptions size="small" bordered column={2}
            items={[
              { label: 'Planificarea', children: <>{p.denumire} {p.codSursa && <Tag>{p.codSursa}</Tag>}</>, span: 2 },
              { label: 'Data actualizării', children: formatData(p.dataActualizare) },
              { label: 'Perioada', children: `${formatData(p.start)} – ${formatData(p.sfarsit)}` },
              { label: 'Structura', children: `${p.noduriWbs} noduri WBS, ${p.activitati} activități, ${p.legaturi} legături` },
              { label: 'Execuție', children: `${p.procent.toLocaleString('ro-RO')}% · ${p.finalizate} finalizate, ${p.inCurs} în curs, ${p.critice} critice` },
              { label: `Cost planificat (${m})`, children: formatLei(p.costPlanificat) },
              { label: `Realizat / rămas (${m})`, children: `${formatLei(p.costRealizat)} / ${formatLei(p.costRamas)}` },
            ]} />

          <div>
            <Typography.Text strong>Unde se importă</Typography.Text>
            <Radio.Group style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 6 }} value={mod === 'nou' ? 'nou' : tinta}
              onChange={(e) => (e.target.value === 'nou' ? schimbaTinta('nou') : schimbaTinta('actualizare', e.target.value as string))}>
              {p.graficExistent && (
                <Radio value={p.graficExistent.id}>
                  Actualizare a graficului <b>{p.graficExistent.denumire}</b> (acum la actualizarea nr. {p.graficExistent.versiuni[0]?.numar},{' '}
                  {formatData(p.graficExistent.versiuni[0]?.dataActualizare)})
                </Radio>
              )}
              {altele.map((g) => (
                <Radio key={g.id} value={g.id}>Actualizare a graficului {g.denumire}{g.codSursa ? ` (${g.codSursa})` : ''}</Radio>
              ))}
              <Radio value="nou">Grafic nou pe proiect</Radio>
            </Radio.Group>
            {mod === 'nou' && (
              <Input style={{ marginTop: 8 }} value={denumire} onChange={(e) => setDenumire(e.target.value)} placeholder="Denumirea graficului" maxLength={300} />
            )}
          </div>

          {mod === 'actualizare' && c && (
            <Alert type={c.decalajSfarsitProiectZile && c.decalajSfarsitProiectZile > 0 ? 'warning' : 'info'} showIcon
              title="Față de actualizarea curentă"
              description={
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  <li>Sfârșitul graficului: {formatData(c.sfarsitAnterior)} → {formatData(c.sfarsit)}
                    {c.decalajSfarsitProiectZile ? <b> ({zile(c.decalajSfarsitProiectZile)})</b> : ' (neschimbat)'}</li>
                  <li>Execuția: {c.procentAnterior.toLocaleString('ro-RO')}% → {c.procent.toLocaleString('ro-RO')}%; {c.finalizate} activități finalizate de atunci</li>
                  <li>{c.modificate.length} activități modificate, dintre care {c.intarziate} cu sfârșitul amânat; {c.noi.length} noi, {c.sterse.length} șterse</li>
                </ul>
              } />
          )}
        </Space>
      )}
    </Modal>
  )
}
