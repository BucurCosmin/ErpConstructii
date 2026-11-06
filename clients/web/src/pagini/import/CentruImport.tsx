import { CheckCircleOutlined, DesktopOutlined, DownloadOutlined, InboxOutlined, MergeCellsOutlined, MoreOutlined, ScissorOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Dropdown, Flex, Modal, Progress, Result, Row, Select, Space, Spin, Tag, Tooltip, Typography, Upload } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { fisierDocument, useAnalizeazaFisier } from '../../api/importDocumente'
import type { AnalizaImport, TipDocumentImport } from '../../api/importDocumente'
import { usePermisiuni } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'
import { formatData } from '../../util/format'
import { ImportNecesarExtern } from '../aprovizionare/ImportNecesarExtern'
import { ImportNotaComanda } from '../aprovizionare/ImportNotaComanda'
import { ImportFactura } from '../facturi/ImportFactura'

interface DocumentLucru {
  id: number
  tip: TipDocumentImport
  pagini: number[]
  numar: string | null
  data: string | null
  /** Ce s-a creat la import (ex. „Factura DQUI 2011”). */
  importat?: string
}

const tipuri: Record<TipDocumentImport, { eticheta: string; plural: string; culoare: string }> = {
  Factura: { eticheta: 'Factură', plural: 'facturi', culoare: 'blue' },
  NotaComanda: { eticheta: 'Notă de comandă', plural: 'note de comandă', culoare: 'purple' },
  NecesarMateriale: { eticheta: 'Necesar de materiale', plural: 'necesare', culoare: 'magenta' },
  BonConsum: { eticheta: 'Bon de consum', plural: 'bonuri de consum', culoare: 'volcano' },
  Anexe: { eticheta: 'Pagini fără document', plural: 'grupuri de anexe', culoare: 'default' },
}

const interval = (ps: number[]) => (ps.length === 1 ? `p. ${ps[0]}` : ps[ps.length - 1] - ps[0] === ps.length - 1 ? `p. ${ps[0]}–${ps[ps.length - 1]}` : `p. ${ps.join(', ')}`)

/**
 * Centrul de import: un fișier scanat poate conține mai multe facturi (fiecare cu e-Factura, avizul și certificatele ei),
 * note de comandă, necesare de materiale și bonuri de consum. Serverul citește fișierul o dată și propune documentele;
 * utilizatorul verifică împărțirea (tip, unire, despărțire), apoi importă fiecare document prin ecranul tipului lui.
 */
export function CentruImport() {
  const { message, modal } = App.useApp()
  const navigheaza = useNavigate()
  const permisiuni = usePermisiuni()
  const drepturi = new Set(permisiuni.data ?? [])
  const poateFacturi = drepturi.has('financiar.creare')
  const poateAprovizionare = drepturi.has('aprovizionare.creare')
  const analizeaza = useAnalizeazaFisier()
  const [fisier, setFisier] = useState<File>()
  const [url, setUrl] = useState<string>()
  const [analiza, setAnaliza] = useState<AnalizaImport>()
  const [documente, setDocumente] = useState<DocumentLucru[]>([])
  const [pagina, setPagina] = useState<number>()
  const [deschis, setDeschis] = useState<{ doc: DocumentLucru; fisier: File }>()
  const [seTaie, setSeTaie] = useState<number>()
  const urmatorId = useRef(1)

  useEffect(() => () => {
    if (url) URL.revokeObjectURL(url)
  }, [url])

  const reseteaza = () => {
    setFisier(undefined)
    setUrl(undefined)
    setAnaliza(undefined)
    setDocumente([])
    setPagina(undefined)
  }

  const citeste = async (f: File) => {
    reseteaza()
    setFisier(f)
    if (/\.(pdf|png|jpe?g)$/i.test(f.name)) setUrl(URL.createObjectURL(f))
    try {
      const a = await analizeaza.mutateAsync(f)
      setAnaliza(a)
      setDocumente(a.documente.map((d) => ({ ...d, id: urmatorId.current++ })))
    } catch (e) {
      message.error((e as Error).message)
      reseteaza()
    }
    return false
  }

  const modifica = (lista: DocumentLucru[]) => setDocumente(lista.filter((d) => d.pagini.length > 0))

  const uneste = (i: number) => {
    const [sus, jos] = [documente[i - 1], documente[i]]
    const unit = { ...sus, pagini: [...sus.pagini, ...jos.pagini].sort((a, b) => a - b), numar: sus.numar ?? jos.numar, data: sus.data ?? jos.data, importat: undefined }
    modifica([...documente.slice(0, i - 1), unit, ...documente.slice(i + 1)])
  }

  const desparte = (i: number, dela: number) => {
    const d = documente[i]
    const primul = { ...d, pagini: d.pagini.filter((p) => p < dela), importat: undefined }
    const alDoilea: DocumentLucru = { id: urmatorId.current++, tip: d.tip, pagini: d.pagini.filter((p) => p >= dela), numar: null, data: null }
    modifica([...documente.slice(0, i), primul, alDoilea, ...documente.slice(i + 1)])
  }

  const schimbaTip = (i: number, tip: TipDocumentImport) =>
    modifica(documente.map((d, j) => (j === i ? { ...d, tip, importat: undefined } : d)))

  const deschide = async (doc: DocumentLucru) => {
    if (!analiza) return
    setSeTaie(doc.id)
    try {
      setDeschis({ doc, fisier: await fisierDocument(analiza, doc.pagini) })
    } catch (e) {
      message.error((e as Error).message)
    } finally {
      setSeTaie(undefined)
    }
  }

  const importat = (id: number, descriere: string) =>
    setDocumente((lista) => lista.map((d) => (d.id === id ? { ...d, importat: descriere } : d)))

  /** Descarcă ErpImport.exe doar dacă e publicat pe server (altfel browserul ar salva pagina aplicației ca .exe). */
  const descarcaAplicatia = async () => {
    const url = '/descarcari/ErpImport.exe'
    try {
      const r = await fetch(url, { method: 'HEAD' })
      if (!r.ok || (r.headers.get('content-type') ?? '').startsWith('text/html')) {
        modal.warning({
          title: 'Aplicația Windows nu este publicată pe acest server',
          content: 'Administratorul rulează o dată deploy\\pregateste-productie.ps1 pe calculatorul serverului: publică ErpImport.exe aici. ' +
            'În dezvoltare, aplicația se pornește cu: dotnet run --project clients/desktop/ErpConstructii.ImportDesktop',
        })
        return
      }
    } catch {
      message.error('Serverul nu răspunde.')
      return
    }

    window.location.href = url
    modal.info({
      title: 'După descărcare',
      content: (
        <ol style={{ paddingInlineStart: 18, margin: 0 }}>
          <li>Pornește <b>ErpImport.exe</b> din Descărcări (îl poți muta oriunde, nu cere instalare).</li>
          <li>Dacă Windows afișează „Windows a protejat computerul”: <b>Mai multe informații → Rulează oricum</b> (aplicația nu e semnată digital).</li>
          <li>După prima pornire, butonul „Deschide în aplicația Windows” o deschide direct, conectată la acest server.</li>
        </ol>
      ),
    })
  }

  const antet = (
    <AntetPagina
      titlu="Import documente"
      subtitlu="Un fișier scanat cu mai multe documente: facturi (cu e-Factura, aviz, certificate), note de comandă, necesare de materiale, bonuri de consum."
      cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Import documente' }]}
      actiuni={
        <>
          <Tooltip title="Aplicația Windows de import: același flux, cu formularul lângă paginile documentului. Prima dată descarc-o și pornește-o o dată.">
            <Button icon={<DesktopOutlined />} href={`erp-import://deschide?server=${encodeURIComponent(window.location.origin)}`}>
              Deschide în aplicația Windows
            </Button>
          </Tooltip>
          <Button type="link" icon={<DownloadOutlined />} onClick={() => void descarcaAplicatia()}>
            Descarcă aplicația Windows
          </Button>
        </>
      }
    />
  )

  if (!fisier) {
    return (
      <>
        {antet}
        <Card>
          <Upload.Dragger
            accept=".pdf,.png,.jpg,.jpeg,.tif,.tiff,.docx,.xml,.zip"
            multiple={false}
            showUploadList={false}
            beforeUpload={citeste}
            disabled={!poateFacturi && !poateAprovizionare}
            style={{ padding: '32px 0' }}
          >
            <p className="ant-upload-drag-icon"><InboxOutlined /></p>
            <p className="ant-upload-text">Trage aici fișierul sau apasă pentru a-l alege</p>
            <p className="ant-upload-hint">
              PDF-ul scanat (oricâte documente, în orice ordine), o poză, un document Word sau XML-ul e-Factura.
              Documentele se recunosc automat; verifici împărțirea, apoi le imporți pe rând.
            </p>
          </Upload.Dragger>
        </Card>
      </>
    )
  }

  const paginiFisier = new Map((analiza?.pagini ?? []).map((p) => [p.pagina, p]))
  const deImportat = documente.filter((d) => d.tip !== 'BonConsum' && d.tip !== 'Anexe')
  const importate = deImportat.filter((d) => d.importat).length
  const numarare = Object.entries(
    documente.reduce<Record<string, number>>((acc, d) => ({ ...acc, [d.tip]: (acc[d.tip] ?? 0) + 1 }), {}),
  ).map(([tip, n]) => `${n} ${n === 1 ? tipuri[tip as TipDocumentImport].eticheta.toLowerCase() : tipuri[tip as TipDocumentImport].plural}`)

  return (
    <>
      {antet}
      <Row gutter={16}>
        <Col xs={24} xl={10}>
          <Card
            size="small"
            title={<Typography.Text ellipsis style={{ maxWidth: 340 }}>{fisier.name}</Typography.Text>}
            extra={<Button size="small" onClick={reseteaza}>Alt fișier</Button>}
            styles={{ body: { padding: 0 } }}
            style={{ position: 'sticky', top: 80 }}
          >
            {url && /\.pdf$/i.test(fisier.name) ? (
              <iframe key={pagina ?? 0} src={pagina ? `${url}#page=${pagina}` : url} title="Fișierul importat" style={{ width: '100%', height: 780, border: 0, display: 'block' }} />
            ) : url ? (
              <img src={url} alt="Fișierul importat" style={{ width: '100%', display: 'block' }} />
            ) : (
              <Result status="info" title="Fără previzualizare" subTitle="Documentul Word / XML se vede în ecranul de import." />
            )}
          </Card>
        </Col>

        <Col xs={24} xl={14}>
          {analizeaza.isPending || !analiza ? (
            <Card>
              <Flex vertical align="center" gap={12} style={{ padding: 48 }}>
                <Spin size="large" />
                <Typography.Text>Se citește fișierul…</Typography.Text>
                <Typography.Text type="secondary">Paginile scanate trec prin recunoașterea textului: câteva secunde pe pagină.</Typography.Text>
              </Flex>
            </Card>
          ) : (
            <>
              <Card size="small" style={{ marginBottom: 16 }}>
                <Flex justify="space-between" align="center" gap={16} wrap>
                  <Space direction="vertical" size={0}>
                    <Typography.Text strong>
                      {documente.length === 1 ? 'Un document' : `${documente.length} documente`} în {analiza.pagini.length === 1 ? 'o pagină' : `${analiza.pagini.length} pagini`}
                    </Typography.Text>
                    <Typography.Text type="secondary">{numarare.join(', ')}</Typography.Text>
                  </Space>
                  {deImportat.length > 0 && (
                    <Space>
                      <Progress type="circle" size={44} percent={Math.round((importate / deImportat.length) * 100)} format={() => `${importate}/${deImportat.length}`} />
                      <Typography.Text type="secondary">importate</Typography.Text>
                    </Space>
                  )}
                </Flex>
              </Card>

              {analiza.probleme.length > 0 && (
                <Alert type="warning" showIcon style={{ marginBottom: 16 }} title={
                  <ul style={{ margin: 0, paddingInlineStart: 18 }}>{analiza.probleme.map((p, i) => <li key={i}>{p.mesaj}</li>)}</ul>
                } />
              )}
              {analiza.separabil && documente.length > 0 && (
                <Alert type="info" showIcon style={{ marginBottom: 16 }}
                  title="Verifică împărțirea înainte de import: poți schimba tipul unui document, îl poți uni cu cel de deasupra sau despărți. Avizul și certificatele rămân la factura lor." />
              )}

              <Space direction="vertical" style={{ width: '100%' }} size={10}>
                {documente.map((d, i) => {
                  const poate = d.tip === 'Factura' ? poateFacturi : poateAprovizionare
                  const titlu = [tipuri[d.tip].eticheta, d.numar && `nr. ${d.numar}`, d.data && `din ${formatData(d.data)}`].filter(Boolean).join(' ')
                  return (
                    <Card
                      key={d.id}
                      size="small"
                      style={d.importat ? { borderColor: '#b7eb8f', background: '#f6ffed' } : undefined}
                      title={
                        <Space size={8}>
                          <Typography.Text type="secondary">{i + 1}.</Typography.Text>
                          <Tag color={tipuri[d.tip].culoare} style={{ marginInlineEnd: 0 }}>{interval(d.pagini)}</Tag>
                          <Typography.Text strong>{titlu}</Typography.Text>
                        </Space>
                      }
                      extra={
                        <Space size={6}>
                          <Select<TipDocumentImport>
                            size="small"
                            value={d.tip}
                            onChange={(t) => schimbaTip(i, t)}
                            disabled={!!d.importat}
                            style={{ width: 170 }}
                            options={(Object.keys(tipuri) as TipDocumentImport[]).map((t) => ({ value: t, label: tipuri[t].eticheta }))}
                          />
                          <Dropdown
                            disabled={!!d.importat}
                            menu={{
                              items: [
                                { key: 'uneste', icon: <MergeCellsOutlined />, label: 'Unește cu documentul de deasupra', disabled: i === 0 || !!documente[i - 1]?.importat },
                                ...(d.pagini.length > 1
                                  ? [{
                                      key: 'desparte',
                                      icon: <ScissorOutlined />,
                                      label: 'Desparte',
                                      children: d.pagini.slice(1).map((p) => ({ key: `p${p}`, label: `Document nou de la p. ${p}` })),
                                    }]
                                  : []),
                              ],
                              onClick: ({ key }) => (key === 'uneste' ? uneste(i) : desparte(i, Number(key.slice(1)))),
                            }}
                          >
                            <Button size="small" icon={<MoreOutlined />} aria-label="Unește sau desparte" />
                          </Dropdown>
                        </Space>
                      }
                    >
                      <Flex justify="space-between" align="center" gap={12} wrap>
                        <Space size={[4, 4]} wrap>
                          {d.pagini.map((p) => (
                            <Tooltip key={p} title="Arată pagina">
                              <Button size="small" type={pagina === p ? 'primary' : 'default'} onClick={() => setPagina(p)}>
                                p. {p} · {paginiFisier.get(p)?.descriere ?? 'pagină'}
                              </Button>
                            </Tooltip>
                          ))}
                        </Space>
                        {d.importat ? (
                          <Tag color="green" icon={<CheckCircleOutlined />}>{d.importat} — importat</Tag>
                        ) : d.tip === 'BonConsum' ? (
                          <Tooltip title="Bonul de consum se înregistrează în Stocuri → Consum, pe articolele devizului.">
                            <Button size="small" onClick={() => navigheaza('/stocuri')}>Deschide Stocuri</Button>
                          </Tooltip>
                        ) : d.tip === 'Anexe' ? (
                          <Typography.Text type="secondary">Nu e un document de importat</Typography.Text>
                        ) : (
                          <Tooltip title={poate ? undefined : d.tip === 'Factura' ? 'Ai nevoie de dreptul financiar.creare.' : 'Ai nevoie de dreptul aprovizionare.creare.'}>
                            <Button type="primary" size="small" loading={seTaie === d.id} disabled={!poate} onClick={() => void deschide(d)}>
                              Importă
                            </Button>
                          </Tooltip>
                        )}
                      </Flex>
                    </Card>
                  )
                })}
              </Space>
            </>
          )}
        </Col>
      </Row>

      <Modal
        open={!!deschis}
        onCancel={() => setDeschis(undefined)}
        footer={null}
        width="min(1500px, 96vw)"
        destroyOnHidden
        title={deschis && `${tipuri[deschis.doc.tip].eticheta} · ${interval(deschis.doc.pagini)} din ${fisier.name}`}
      >
        {deschis?.doc.tip === 'Factura' && (
          <ImportFactura poateCrea={poateFacturi} fisierInitial={deschis.fisier} onImportat={(t) => importat(deschis.doc.id, t)} />
        )}
        {deschis?.doc.tip === 'NotaComanda' && (
          <ImportNotaComanda poateCrea={poateAprovizionare} fisierInitial={deschis.fisier} onImportat={(t) => importat(deschis.doc.id, t)} />
        )}
        {deschis?.doc.tip === 'NecesarMateriale' && (
          <ImportNecesarExtern poateCrea={poateAprovizionare} fisierInitial={deschis.fisier} onImportat={(t) => importat(deschis.doc.id, t)} />
        )}
      </Modal>
    </>
  )
}
