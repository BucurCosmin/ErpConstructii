import { InboxOutlined, PaperClipOutlined } from '@ant-design/icons'
import { Alert, App, Button, Col, Collapse, Descriptions, Input, Modal, Row, Select, Space, Steps, Table, Tag, Typography, Upload } from 'antd'
import { useState } from 'react'
import { useConfirmaImport, useDevize, usePrevizualizareImport } from '../../api/interogari'
import type {
  ArticolImport, ComparatieTotal, DiferentaArticol, PrevizualizareImport, ProblemaImport, ResursaImport, SectiuneImport, TipResursa, TipVersiuneDeviz,
} from '../../api/tipuri'
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

const componenteDeviz: { value: TipResursa; label: string }[] = [
  { value: 'Material', label: 'Material' },
  { value: 'Manopera', label: 'Manoperă' },
  { value: 'Utilaj', label: 'Utilaj' },
  { value: 'Transport', label: 'Transport' },
  { value: 'Echipament', label: 'Echipament' },
]

/** Valoare „deviz nou” în alegerea devizului țintă (importul pe obiecte). */
const DevizNou = 'nou'

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
  // Importul pe obiecte (BOQ, D-58): componentele de preț alese și devizul în care intră obiectul.
  const [componente, setComponente] = useState<Record<string, TipResursa>>({})
  const [tinta, setTinta] = useState<string>()
  const devize = useDevize(proiectId)
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
    setComponente({})
    setTinta(undefined)
    laInchidere()
  }

  // Devizul propus pentru importul pe obiecte: cel mai nou deviz BOQ în vigoare al proiectului (sau ciorna lui de lucru).
  const lista = devize.data ?? []
  const deBoq = new Set(lista.filter((v) => v.sursa === 'import:boq').map((v) => v.id))
  const tintaPropusa = lista.filter((v) => v.stare !== 'Inlocuita' && (deBoq.has(v.id) || (v.versiuneAnterioaraId && deBoq.has(v.versiuneAnterioaraId))))
    .sort((a, b) => b.numar - a.numar)[0]?.id
  const tintaAleasa = (r: PrevizualizareImport | null, t = tinta) => (r?.peObiecte ? (t ?? tintaPropusa ?? DevizNou) : DevizNou)

  // Cursul și moneda se trimit doar când nu există F3 (cu F3, cursul se deduce și moneda vine din listă) sau când sunt date explicit.
  const optiuni = (deviz: File, lista: File | null, alese = componente, t?: string) => ({
    fisier: deviz, f3: lista, curs, moneda: curs ? moneda : null, componente: alese,
    versiuneTintaId: t && t !== DevizNou ? t : null,
  })

  const verifica = async (deviz: File | null, lista: File | null, alese = componente, t?: string) => {
    if (!deviz) return
    setRezultat(null)
    setEroare(null)
    try {
      // Prima citire spune dacă fișierul e un obiect (BOQ); atunci se recitește cu devizul țintă propus.
      let r = await previzualizeaza.mutateAsync(optiuni(deviz, lista, alese, t))
      const propusa = tintaAleasa(r, t)
      if (r.peObiecte && !t && propusa !== DevizNou) r = await previzualizeaza.mutateAsync(optiuni(deviz, lista, alese, propusa))
      setRezultat(r)
    } catch (e) {
      setEroare((e as Error).message)
    }
  }

  const alegeComponenta = (cheie: string, tipNou: TipResursa) => {
    const alese = { ...componente, [cheie]: tipNou }
    setComponente(alese)
    void verifica(fisier, f3, alese, tintaAleasa(rezultat))
  }

  const alegeTinta = (t: string) => {
    setTinta(t)
    void verifica(fisier, f3, componente, t)
  }

  const salveaza = async () => {
    if (!fisier) return
    try {
      const t = tintaAleasa(rezultat)
      const r = await confirma.mutateAsync({ ...optiuni(fisier, f3, componente, t), tip, denumire })
      if (r.obiect)
        message.success(`Obiectul ${r.obiect} a fost ${r.inlocuit ? 'înlocuit' : 'adăugat'} în ${r.ciornaNoua ? 'ciorna nouă de lucru' : 'ciorna de lucru'} nr. ${r.numar}.`)
      else
        message.success(`Devizul a fost importat ca ${tipuriVersiune[tip].toLowerCase()} nr. ${r.numar}.`)
      inchide()
    } catch (e) {
      setEroare((e as Error).message)
    }
  }

  const deAles = rezultat?.componente?.filter((c) => !c.tip) ?? []
  const peObiectExistent = rezultat?.peObiecte && tintaAleasa(rezultat) !== DevizNou

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
            <p className="ant-upload-hint">
              Din ISDP: raportul „Deviz ofertă cu resurse” (.xls). Sau o ofertă BOQ pe obiecte (.xlsx, foaia BOQ + foile pe secțiuni), importată câte un obiect.
            </p>
          </Upload.Dragger>
        </Col>
        {!rezultat?.peObiecte && <Col xs={24} md={10}>
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
        </Col>}
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
              rezultat.peObiecte && rezultat.sectiuni
                ? { label: 'Valoare ofertă', children: `${formatLei(rezultat.sectiuni.reduce((x, r) => x + r.calculat, 0))} ${rezultat.moneda} (cost direct gol)` }
                : { label: 'Total fără TVA', children: rezultat.recapitulatie ? `${formatLei(rezultat.recapitulatie.totalFaraTva)} lei` : '—' },
            ]}
          />

          {rezultat.peObiecte && (
            <Space orientation="vertical" size={8} style={{ width: '100%' }}>
              {rezultat.componente && rezultat.componente.length > 0 && (
                <Space wrap>
                  <Typography.Text strong>Componentele de preț:</Typography.Text>
                  {rezultat.componente.map((c) => (
                    <Space.Compact key={c.cheie}>
                      <Input readOnly value={c.eticheta} style={{ width: 130 }} status={c.tip ? undefined : 'error'} />
                      <Select<TipResursa> value={c.tip ?? undefined} placeholder="alege componenta" style={{ width: 150 }} status={c.tip ? undefined : 'error'}
                        onChange={(v) => alegeComponenta(c.cheie, v)} options={componenteDeviz} />
                    </Space.Compact>
                  ))}
                </Space>
              )}
              {deAles.length > 0 && (
                <Alert type="warning" showIcon
                  title={`Nu e clar pe ce componentă a devizului merge ${deAles.map((c) => `„${c.eticheta}”`).join(', ')}: alege-o mai sus (ex. utilaj, transport) ca să poți confirma importul.`} />
              )}
              <Space wrap>
                <Typography.Text strong>Obiectul {rezultat.tinta?.obiect ?? ''} intră în:</Typography.Text>
                <Select style={{ minWidth: 380 }} value={tintaAleasa(rezultat)} onChange={alegeTinta}
                  options={[
                    { value: DevizNou, label: 'Deviz nou (primul obiect)' },
                    ...lista.filter((v) => v.stare !== 'Inlocuita').sort((a, b) => b.numar - a.numar)
                      .map((v) => ({ value: v.id, label: `${tipuriVersiune[v.tip]} nr. ${v.numar} — ${v.denumire}${v.versiuneAnterioaraId ? ' (ciornă de lucru)' : ''}` })),
                  ]} />
              </Space>
              {rezultat.tinta && (
                <Alert type={rezultat.tinta.inlocuieste ? 'warning' : 'info'} showIcon
                  title={rezultat.tinta.inlocuieste
                    ? `Obiectul ${rezultat.tinta.obiect} există deja în ${rezultat.tinta.versiune}: se înlocuiește (articolele potrivite își păstrează legăturile).`
                    : `Obiectul ${rezultat.tinta.obiect} se adaugă în ${rezultat.tinta.versiune}.`}
                  description={`Obiecte existente: ${rezultat.tinta.obiecte.join('; ') || '—'}. ${rezultat.tinta.versiuneLucruId
                    ? 'Se modifică ciorna de lucru a devizului.' : 'Se creează ciorna de lucru (devizul ales rămâne neschimbat; diferențele se văd și se aprobă ca la editare).'}`} />
              )}
            </Space>
          )}

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
            defaultActiveKey={[rezultat.sectiuni ? 'sectiuni' : 'totaluri']}
            items={[
              ...(rezultat.sectiuni ? [{
                key: 'sectiuni',
                label: `Secțiunile (${rezultat.sectiuni.length}) — totaluri în ${rezultat.moneda}`,
                children: (
                  <Table<SectiuneImport>
                    size="small"
                    rowKey="sursa"
                    pagination={false}
                    dataSource={rezultat.sectiuni}
                    summary={(s) => (
                      <Table.Summary.Row>
                        <Table.Summary.Cell index={0} colSpan={3}><b>Total</b></Table.Summary.Cell>
                        <Table.Summary.Cell index={3} align="right"><b>{s.reduce((x, r) => x + r.articole, 0)}</b></Table.Summary.Cell>
                        <Table.Summary.Cell index={4} align="right"><b>{formatLei(s.reduce((x, r) => x + r.calculat, 0))}</b></Table.Summary.Cell>
                        <Table.Summary.Cell index={5} colSpan={3} />
                      </Table.Summary.Row>
                    )}
                    columns={[
                      { title: 'Cod', dataIndex: 'cod', width: 110 },
                      { title: 'Secțiunea', dataIndex: 'denumire', ellipsis: true },
                      { title: 'Foaia', dataIndex: 'sursa', width: 150, ellipsis: true },
                      { title: 'Articole', dataIndex: 'articole', width: 80, align: 'right' },
                      { title: 'Articolele dau', dataIndex: 'calculat', width: 140, align: 'right', render: formatLei },
                      { title: 'TOTAL foaie', dataIndex: 'declarat', width: 140, align: 'right', render: suma },
                      { title: 'Rezumat BOQ', dataIndex: 'declaratRezumat', width: 140, align: 'right', render: suma },
                      {
                        title: '', key: 'ok', width: 50,
                        render: (_, s) => {
                          const d = s.declaratRezumat ?? s.declarat
                          return d === null ? '' : Math.abs(d - s.calculat) <= 1 ? <Tag color="green">✓</Tag> : <Tag color="red">≠</Tag>
                        },
                      },
                    ]}
                  />
                ),
              }] : []),
              ...(rezultat.ignorate && rezultat.ignorate.length > 0 ? [{
                key: 'ignorate',
                label: `Foi lăsate deoparte (${rezultat.ignorate.length})`,
                children: (
                  <ul style={{ margin: 0, paddingLeft: 18 }}>
                    {rezultat.ignorate.map((i) => <li key={i.sursa}><b>{i.sursa}</b>: {i.motiv}</li>)}
                  </ul>
                ),
              }] : []),
              ...(rezultat.peObiecte ? [] : [{
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
              }]),
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

          {!peObiectExistent && <Space wrap>
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
              {nemapate ? `${nemapate} resurse rămân de legat de nomenclator (nu blochează importul).` : rezultat.peObiecte ? '' : 'Toate resursele sunt legate de nomenclator.'}
            </Typography.Text>
          </Space>}
        </Space>
      )}
    </Modal>
  )
}
