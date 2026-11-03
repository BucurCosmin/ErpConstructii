import { InboxOutlined } from '@ant-design/icons'
import { Alert, App, Descriptions, Modal, Select, Space, Steps, Table, Tag, Upload } from 'antd'
import { useState } from 'react'
import { useConfirmaImport, usePrevizualizareImport } from '../../api/interogari'
import type { PrevizualizareImport, ProblemaImport, TipVersiuneDeviz } from '../../api/tipuri'
import { formatLei, tipuriVersiune } from '../../util/format'

interface Props {
  proiectId: string
  deschis: boolean
  laInchidere: () => void
}

const culoriSeveritate: Record<ProblemaImport['severitate'], string> = {
  Eroare: 'red',
  Avertisment: 'orange',
  Informare: 'blue',
}

/** Import deviz în doi pași (REQ-IMP-03): previzualizare cu validare, apoi confirmare ca versiune nouă. */
export function ImportDeviz({ proiectId, deschis, laInchidere }: Props) {
  const [fisier, setFisier] = useState<File | null>(null)
  const [tip, setTip] = useState<TipVersiuneDeviz>('Oferta')
  const [rezultat, setRezultat] = useState<PrevizualizareImport | null>(null)
  const [eroare, setEroare] = useState<string | null>(null)
  const previzualizeaza = usePrevizualizareImport(proiectId)
  const confirma = useConfirmaImport(proiectId)
  const { message } = App.useApp()

  const inchide = () => {
    setFisier(null)
    setRezultat(null)
    setEroare(null)
    laInchidere()
  }

  const alege = async (f: File) => {
    setFisier(f)
    setRezultat(null)
    setEroare(null)
    try {
      setRezultat(await previzualizeaza.mutateAsync(f))
    } catch (e) {
      setEroare((e as Error).message)
    }
    return false
  }

  const salveaza = async () => {
    if (!fisier) return
    try {
      const r = await confirma.mutateAsync({ fisier, tip })
      message.success(`Devizul a fost importat ca ${tipuriVersiune[tip].toLowerCase()} nr. ${r.numar}.`)
      inchide()
    } catch (e) {
      setEroare((e as Error).message)
    }
  }

  return (
    <Modal
      title="Import deviz"
      open={deschis}
      onCancel={inchide}
      width={820}
      okText="Confirmă importul"
      cancelText="Renunță"
      okButtonProps={{ disabled: !rezultat?.poateFiConfirmat, loading: confirma.isPending }}
      onOk={salveaza}
      destroyOnHidden
    >
      <Steps
        size="small"
        current={rezultat ? (rezultat.poateFiConfirmat ? 2 : 1) : 0}
        style={{ marginBlock: 16 }}
        items={[{ title: 'Alege fișierul' }, { title: 'Verificare' }, { title: 'Confirmare' }]}
      />

      <Upload.Dragger
        accept=".xml,.xlsx,.xls,.csv,.txt,.dvz,.wd"
        multiple={false}
        showUploadList={false}
        beforeUpload={alege}
        disabled={previzualizeaza.isPending}
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{fisier ? fisier.name : 'Trage fișierul aici sau apasă pentru a-l alege'}</p>
        <p className="ant-upload-hint">Formatul se recunoaște automat (WinDoc și alte programe de devizare, pe măsură ce sunt adăugate).</p>
      </Upload.Dragger>

      {eroare && <Alert type="error" showIcon title={eroare} style={{ marginTop: 16 }} />}

      {rezultat && (
        <Space orientation="vertical" size={16} style={{ width: '100%', marginTop: 16 }}>
          <Descriptions
            size="small"
            bordered
            column={2}
            items={[
              { label: 'Format', children: rezultat.format },
              { label: 'Denumire', children: rezultat.denumire || '—' },
              { label: 'Obiecte', children: rezultat.obiecte },
              { label: 'Articole', children: rezultat.articole },
              { label: 'Total fără TVA', children: rezultat.recapitulatie ? `${formatLei(rezultat.recapitulatie.totalFaraTva)} lei` : '—' },
              { label: 'Total cu TVA', children: rezultat.recapitulatie ? `${formatLei(rezultat.recapitulatie.totalCuTva)} lei` : '—' },
            ]}
          />

          {rezultat.probleme.length > 0 && (
            <Table<ProblemaImport>
              size="small"
              rowKey={(p) => `${p.locatie}-${p.mesaj}`}
              dataSource={rezultat.probleme}
              pagination={{ pageSize: 8, hideOnSinglePage: true }}
              columns={[
                { title: 'Severitate', dataIndex: 'severitate', width: 120, render: (s: ProblemaImport['severitate']) => <Tag color={culoriSeveritate[s]}>{s}</Tag> },
                { title: 'Locație', dataIndex: 'locatie', width: 160 },
                { title: 'Mesaj', dataIndex: 'mesaj' },
              ]}
            />
          )}

          <Space>
            Importă ca:
            <Select<TipVersiuneDeviz>
              value={tip}
              onChange={setTip}
              style={{ width: 220 }}
              options={(Object.keys(tipuriVersiune) as TipVersiuneDeviz[]).map((t) => ({ value: t, label: tipuriVersiune[t] }))}
            />
          </Space>
        </Space>
      )}
    </Modal>
  )
}
