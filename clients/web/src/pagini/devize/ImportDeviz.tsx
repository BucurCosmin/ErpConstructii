import { InboxOutlined, PaperClipOutlined } from '@ant-design/icons'
import { Alert, App, Button, Col, Collapse, Descriptions, Input, Modal, Row, Select, Space, Steps, Table, Tag, Typography, Upload } from 'antd'
import { useState } from 'react'
import { useConfirmaImport, usePrevizualizareImport } from '../../api/interogari'
import type { ArticolImport, ComparatieTotal, DiferentaArticol, PrevizualizareImport, ProblemaImport, ResursaImport, TipVersiuneDeviz } from '../../api/tipuri'
import { NumarRo } from '../../componente/NumarRo'
import { formatCantitate, formatLei, tipuriVersiune } from '../../util/format'

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

const nomenclator: Record<ResursaImport['nomenclator'], { eticheta: string; culoare: string }> = {
  Noua: { eticheta: 'nouă (nemapată)', culoare: 'gold' },
  Nemapata: { eticheta: 'nemapată', culoare: 'orange' },
  Mapata: { eticheta: 'mapată', culoare: 'green' },
}

const stariReimport: Record<DiferentaArticol['stare'], { eticheta: string; culoare: string }> = {
  Neschimbat: { eticheta: 'neschimbat', culoare: 'default' },
  Nou: { eticheta: 'nou', culoare: 'green' },
  Eliminat: { eticheta: 'eliminat', culoare: 'red' },
  CantitateSchimbata: { eticheta: 'cantitate', culoare: 'blue' },
  PretSchimbat: { eticheta: 'preț', culoare: 'purple' },
  CantitateSiPretSchimbate: { eticheta: 'cantitate + preț', culoare: 'magenta' },
}

const suma = (v: number | null | undefined) => (v === null || v === undefined ? '—' : formatLei(v))

/**
 * Importul devizului în proiect (REQ-IMP-03, D-50): devizul ISDP „Deviz ofertă cu resurse” (.xls) și, opțional, lista F3
 * (.xlsx), care dă cursul și prețurile de ofertă. Previzualizarea arată recapitulația (fișier vs. recalcul), problemele,
 * extrasul de resurse cu starea în nomenclator și, la reimport, diferențele față de versiunea existentă; confirmarea
 * creează versiunea (ciornă), cu fișierele originale arhivate.
 */
export function ImportDeviz({ proiectId, deschis, laInchidere }: Props) {
  const [fisier, setFisier] = useState<File | null>(null)
  const [f3, setF3] = useState<File | null>(null)
  const [curs, setCurs] = useState<number | null>(null)
  const [moneda, setMoneda] = useState<string>('EUR')
  const [tip, setTip] = useState<TipVersiuneDeviz>('Oferta')
  const [denumire, setDenumire] = useState('')
  const [rezultat, setRezultat] = useState<PrevizualizareImport | null>(null)
  const [eroare, setEroare] = useState<string | null>(null)
  const previzualizeaza = usePrevizualizareImport(proiectId)
  const confirma = useConfirmaImport(proiectId)
  const { message } = App.useApp()

  const inchide = () => {
    setFisier(null)
    setF3(null)
    setCurs(null)
    setDenumire('')
    setRezultat(null)
    setEroare(null)
    laInchidere()
  }

  // Cursul și moneda se trimit doar când nu există F3 (cu F3, cursul se deduce și moneda vine din listă) sau când sunt date explicit.
  const optiuni = (deviz: File, lista: File | null) => ({ fisier: deviz, f3: lista, curs, moneda: curs ? moneda : null })

  const verifica = async (deviz: File | null, lista: File | null) => {
    if (!deviz) return
    setRezultat(null)
    setEroare(null)
    try {
      setRezultat(await previzualizeaza.mutateAsync(optiuni(deviz, lista)))
    } catch (e) {
      setEroare((e as Error).message)
    }
  }

  const salveaza = async () => {
    if (!fisier) return
    try {
      const r = await confirma.mutateAsync({ ...optiuni(fisier, f3), tip, denumire })
      message.success(`Devizul a fost importat ca ${tipuriVersiune[tip].toLowerCase()} nr. ${r.numar}.`)
      inchide()
    } catch (e) {
      setEroare((e as Error).message)
    }
  }

  const nemapate = rezultat?.resurse?.filter((r) => r.nomenclator !== 'Mapata').length ?? 0

  return (
    <Modal
      title="Import deviz"
      open={deschis}
      onCancel={inchide}
      width={1100}
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
        items={[{ title: 'Alege fișierele' }, { title: 'Verificare' }, { title: 'Confirmare' }]}
      />

      <Row gutter={16}>
        <Col xs={24} md={14}>
          <Upload.Dragger
            accept=".xls,.xlsx,.xml"
            multiple={false}
            showUploadList={false}
            beforeUpload={(f) => {
              setFisier(f)
              void verifica(f, f3)
              return false
            }}
            disabled={previzualizeaza.isPending}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p className="ant-upload-text">{fisier ? fisier.name : 'Devizul: trage fișierul aici sau apasă'}</p>
            <p className="ant-upload-hint">Din ISDP: raportul „Deviz ofertă cu resurse” (.xls), cu resursele fiecărui articol.</p>
          </Upload.Dragger>
        </Col>
        <Col xs={24} md={10}>
          <Space orientation="vertical" style={{ width: '100%' }}>
            <Upload
              accept=".xlsx,.xls"
              multiple={false}
              showUploadList={false}
              beforeUpload={(f) => {
                setF3(f)
                void verifica(fisier, f)
                return false
              }}
            >
              <Button icon={<PaperClipOutlined />} disabled={previzualizeaza.isPending}>
                {f3 ? `F3: ${f3.name}` : 'Adaugă lista F3 (opțional)'}
              </Button>
            </Upload>
            {f3 && (
              <Button type="link" size="small" onClick={() => { setF3(null); void verifica(fisier, null) }}>
                Fără F3
              </Button>
            )}
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              F3 dă moneda ofertei și cursul (dedus), iar prețurile de ofertă se verifică pe ea. Fără F3, prețul de ofertă se calculează în lei
              sau la cursul de mai jos.
            </Typography.Text>
            <Space.Compact>
              <NumarRo value={curs ?? undefined} onChange={(v) => setCurs(v ?? null)} placeholder="Curs (lei / unitate)" style={{ width: 170 }} />
              <Select value={moneda} onChange={setMoneda} style={{ width: 90 }} options={['EUR', 'USD'].map((m) => ({ value: m, label: m }))} />
              <Button onClick={() => void verifica(fisier, f3)} disabled={!fisier}>Recalculează</Button>
            </Space.Compact>
          </Space>
        </Col>
      </Row>

      {eroare && <Alert type="error" showIcon title={eroare} style={{ marginTop: 16 }} />}

      {rezultat && (
        <Space orientation="vertical" size={16} style={{ width: '100%', marginTop: 16 }}>
          <Descriptions
            size="small"
            bordered
            column={3}
            items={[
              { label: 'Format', children: rezultat.format },
              { label: 'Denumire', children: rezultat.denumire || '—', span: 2 },
              { label: 'Capitole', children: rezultat.capitole },
              { label: 'Articole', children: rezultat.articole },
              { label: 'Rotunjire', children: rezultat.politicaRotunjire === 'Fara' ? 'fără rotunjiri intermediare (ca ISDP)' : 'la ban' },
              { label: 'Moneda ofertei', children: rezultat.moneda },
              { label: 'Curs', children: rezultat.curs ? `${rezultat.curs} lei / ${rezultat.moneda}` : '—' },
              { label: 'Total fără TVA', children: rezultat.recapitulatie ? `${formatLei(rezultat.recapitulatie.totalFaraTva)} lei` : '—' },
            ]}
          />

          {rezultat.probleme.length > 0 && (
            <Table<ProblemaImport>
              size="small"
              rowKey={(p) => `${p.locatie}-${p.mesaj}`}
              dataSource={rezultat.probleme}
              pagination={{ pageSize: 6, hideOnSinglePage: true }}
              columns={[
                { title: 'Severitate', dataIndex: 'severitate', width: 120, render: (s: ProblemaImport['severitate']) => <Tag color={culoriSeveritate[s]}>{s}</Tag> },
                { title: 'Locație', dataIndex: 'locatie', width: 220, ellipsis: true },
                { title: 'Mesaj', dataIndex: 'mesaj' },
              ]}
            />
          )}

          {rezultat.reimport && (
            <Alert
              type={rezultat.reimport.noi + rezultat.reimport.eliminate + rezultat.reimport.modificate === 0 ? 'info' : 'warning'}
              showIcon
              title={`Reimport față de ${rezultat.reimport.versiune}`}
              description={`${rezultat.reimport.chei} articole își păstrează legăturile (cheia stabilă) · ${rezultat.reimport.noi} noi · ${rezultat.reimport.eliminate} eliminate · ${rezultat.reimport.modificate} modificate.`}
            />
          )}

          <Collapse
            size="small"
            defaultActiveKey={['totaluri']}
            items={[
              {
                key: 'totaluri',
                label: 'Recapitulația: fișier vs. recalcul (lei)',
                children: (
                  <Table<ComparatieTotal>
                    size="small"
                    rowKey="rand"
                    pagination={false}
                    dataSource={rezultat.totaluri ?? []}
                    columns={[
                      { title: 'Rând', dataIndex: 'rand' },
                      { title: 'În fișier', dataIndex: 'declarat', align: 'right', render: suma },
                      { title: 'Recalculat', dataIndex: 'recalculat', align: 'right', render: suma },
                      {
                        title: '', key: 'ok', width: 60,
                        render: (_, t) => t.declarat === null ? '' : Math.abs(t.declarat - t.recalculat) < 0.005 ? <Tag color="green">✓</Tag> : <Tag color="red">≠</Tag>,
                      },
                    ]}
                  />
                ),
              },
              {
                key: 'resurse',
                label: `Extrasul de resurse (${rezultat.resurse?.length ?? 0}${nemapate ? `, ${nemapate} de legat de nomenclator` : ''})`,
                children: (
                  <Table<ResursaImport>
                    size="small"
                    rowKey="cod"
                    dataSource={rezultat.resurse ?? []}
                    pagination={{ pageSize: 10, hideOnSinglePage: true }}
                    columns={[
                      { title: 'Cod', dataIndex: 'cod', width: 110 },
                      { title: 'Denumire', dataIndex: 'denumire', ellipsis: true },
                      { title: 'Tip', dataIndex: 'tip', width: 110, render: (t: string, r) => <>{t}{r.tipDedus && <Tag style={{ marginInlineStart: 4 }}>dedus</Tag>}</> },
                      { title: 'Cantitate', key: 'q', width: 140, align: 'right', render: (_, r) => `${formatCantitate(r.cantitateTotala)} ${r.unitateMasura}` },
                      { title: 'Valoare (lei)', dataIndex: 'valoare', width: 130, align: 'right', render: formatLei },
                      { title: 'Art.', dataIndex: 'articole', width: 60, align: 'right' },
                      {
                        title: 'Nomenclator', key: 'n', width: 230,
                        render: (_, r) => r.mapare ? <Typography.Text style={{ fontSize: 12 }}>{r.mapare}</Typography.Text> : <Tag color={nomenclator[r.nomenclator].culoare}>{nomenclator[r.nomenclator].eticheta}</Tag>,
                      },
                    ]}
                  />
                ),
              },
              {
                key: 'articole',
                label: `Articolele (${rezultat.articoleLista?.length ?? 0})`,
                children: (
                  <Table<ArticolImport>
                    size="small"
                    rowKey={(a) => `${a.codWbs}-${a.simbol}`}
                    dataSource={rezultat.articoleLista ?? []}
                    pagination={{ pageSize: 10, hideOnSinglePage: true }}
                    columns={[
                      { title: 'WBS', dataIndex: 'codWbs', width: 120 },
                      { title: 'Simbol', dataIndex: 'simbol', width: 110, render: (s: string, a) => <>{s}{a.tip === 'Asimilat' && <Tag style={{ marginInlineStart: 4 }}>asim</Tag>}</> },
                      { title: 'Denumire', dataIndex: 'denumire', ellipsis: true },
                      { title: 'Cantitate', key: 'q', width: 120, align: 'right', render: (_, a) => `${formatCantitate(a.cantitate)} ${a.unitateMasura}` },
                      { title: 'Cost direct (lei)', dataIndex: 'costDirect', width: 140, align: 'right', render: formatLei },
                      { title: `Ofertă (${rezultat.moneda})`, dataIndex: 'valoareOferta', width: 130, align: 'right', render: suma },
                    ]}
                  />
                ),
              },
              ...(rezultat.reimport && rezultat.reimport.diferente.length > 0 ? [{
                key: 'reimport',
                label: `Diferențe față de versiunea existentă (${rezultat.reimport.diferente.length})`,
                children: (
                  <Table<DiferentaArticol>
                    size="small"
                    rowKey={(d) => `${d.stare}-${d.codWbs}-${d.simbol}`}
                    dataSource={rezultat.reimport.diferente}
                    pagination={{ pageSize: 10, hideOnSinglePage: true }}
                    columns={[
                      { title: 'Stare', dataIndex: 'stare', width: 140, render: (s: DiferentaArticol['stare'], d) => <>{<Tag color={stariReimport[s].culoare}>{stariReimport[s].eticheta}</Tag>}{d.resurseSchimbate && <Tag>resurse</Tag>}</> },
                      { title: 'WBS', dataIndex: 'codWbs', width: 120 },
                      { title: 'Simbol', dataIndex: 'simbol', width: 110 },
                      { title: 'Denumire', dataIndex: 'denumire', ellipsis: true },
                      { title: 'Cantitate veche → nouă', key: 'q', width: 190, align: 'right', render: (_, d) => `${d.cantitateVeche ?? '—'} → ${d.cantitateNoua ?? '—'}` },
                    ]}
                  />
                ),
              }] : []),
            ]}
          />

          <Space wrap>
            Denumire:
            <Input value={denumire} onChange={(e) => setDenumire(e.target.value)} maxLength={300} style={{ width: 360 }}
              placeholder={rezultat.denumire || 'Denumirea devizului'} />
            Importă ca:
            <Select<TipVersiuneDeviz>
              value={tip}
              onChange={setTip}
              style={{ width: 220 }}
              options={(Object.keys(tipuriVersiune) as TipVersiuneDeviz[]).map((t) => ({ value: t, label: tipuriVersiune[t] }))}
            />
            <Typography.Text type={nemapate ? 'warning' : 'secondary'}>
              {nemapate ? `${nemapate} resurse rămân de legat de nomenclator (nu blochează importul).` : 'Toate resursele sunt legate de nomenclator.'}
            </Typography.Text>
          </Space>
        </Space>
      )}
    </Modal>
  )
}
