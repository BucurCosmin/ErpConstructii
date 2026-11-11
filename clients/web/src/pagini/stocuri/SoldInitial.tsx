import { CloudUploadOutlined, DeleteOutlined, InboxOutlined } from '@ant-design/icons'
import {
  Alert, App, AutoComplete, Button, Card, Checkbox, Collapse, DatePicker, Descriptions, Drawer, Flex, Popconfirm, Select, Space, Table, Tag, Tooltip,
  Typography, Upload,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  etichetaGestiune, useAnuleazaSoldInitial, useDepozite, useGestiuni, useImportaSoldInitial, useInterpreteazaBalanta, useSolduriInitiale,
  useSolduriInitialeAnulate,
} from '../../api/stocuri'
import type { AsociereGestiune, GestiuneBalanta, InterpretareBalanta, LinieBalanta, LinieSoldInitial, SoldInitial as SoldInitialDoc } from '../../api/stocuri'
import { formatCantitate, formatLei } from '../../util/format'

const data = (d: string | null) => (d ? dayjs(d).format('DD.MM.YYYY') : '—')
const CREEAZA = '__nou__'
const UNITATI = ['buc', 'kg', 'to', 'm', 'ml', 'mp', 'mc', 'l', 'sac', 'set', 'pereche', 'rola', 'cutie', 'pachet'].map((value) => ({ value }))

/** Materialul nou fără U.M. în fișier („Raport rapid stoc”): U.M. se alege în ecran. */
const faraUm = (l: LinieBalanta) => l.stocFinal > 0 && !l.articolId && !l.unitateMasura
/** Același material pe mai multe rânduri (loturi de preț) primește aceeași U.M. */
const cheieMaterial = (l: LinieBalanta) => `${l.codExtern ?? ''}|${l.denumire.trim().toUpperCase()}`

/** Liniile unei gestiuni din fișier: doar cele care se preiau (stoc final pozitiv), cu materialul găsit sau „nou”. */
function LiniiGestiune({ g, cuValori, unitati, onUm }: {
  g: GestiuneBalanta; cuValori: boolean; unitati: Record<string, string>; onUm: (l: LinieBalanta, um: string) => void
}) {
  const linii = g.linii.filter((l) => l.stocFinal > 0)
  return (
    <Table<LinieBalanta>
      size="small"
      rowKey="rand"
      dataSource={linii}
      pagination={{ pageSize: 15, hideOnSinglePage: true, size: 'small' }}
      columns={[
        { title: 'Rând', dataIndex: 'rand', width: 60 },
        { title: 'Denumire (fișier)', dataIndex: 'denumire' },
        { title: 'Cod extern', dataIndex: 'codExtern', width: 110, render: (c: string | null) => c ?? '—' },
        { title: 'Stoc final', width: 110, align: 'right', render: (_, l) => formatCantitate(l.stocFinal) },
        {
          title: 'U.M.', width: 110,
          render: (_, l) => faraUm(l)
            ? (
              <AutoComplete size="small" style={{ width: 95 }} options={UNITATI} placeholder="alege" value={unitati[cheieMaterial(l)]}
                status={unitati[cheieMaterial(l)]?.trim() ? undefined : 'warning'} onChange={(v: string) => onUm(l, v)} />
            )
            : l.unitateMasura || l.articolUnitateMasura,
        },
        ...(cuValori ? [
          { title: 'Preț unitar', width: 110, align: 'right' as const, render: (_: unknown, l: LinieBalanta) => (l.pretUnitar == null ? '—' : formatCantitate(l.pretUnitar)) },
          { title: 'Valoare', width: 120, align: 'right' as const, render: (_: unknown, l: LinieBalanta) => formatLei(l.valoare ?? 0) },
        ] : []),
        {
          title: 'Material în ERP', width: 260,
          render: (_, l) => l.articolId
            ? <span>{l.articolCod} {l.articolDenumire} <Tag>{l.sursa === 'CodExtern' ? 'după cod' : 'după denumire'}</Tag></span>
            : <Tag color="blue">nou</Tag>,
        },
      ]}
    />
  )
}

/** Fișierul citit: gestiunile din el, depozitul ales pentru fiecare, data soldului; confirmarea creează documentele SI. */
function PrevizualizareBalanta({ fisier, rezultat, onGata }: { fisier: File; rezultat: InterpretareBalanta; onGata: () => void }) {
  const { message } = App.useApp()
  const gestiuni = useGestiuni()
  const depozite = useDepozite()
  const importa = useImportaSoldInitial()
  const [depozitId, setDepozitId] = useState<string | null>(rezultat.depozitIdPropus)
  const [dataSold, setDataSold] = useState<Dayjs | null>(rezultat.data ? dayjs(rezultat.data) : null)
  const [alegeri, setAlegeri] = useState<Record<string, { gestiuneId: string; importa: boolean }>>(() =>
    Object.fromEntries(rezultat.gestiuni.map((g) => [g.cod, {
      gestiuneId: g.gestiuneId ?? CREEAZA,
      // Gestiunea cu alt sold inițial pe aceeași categorie (ex. balanța fără valori) nu se bifează: stocul s-ar dubla.
      importa: g.liniiCuStoc > 0 && !g.dejaImportat && !g.alteSolduri?.length,
    }])))
  // U.M. materialelor noi din „Raport rapid stoc”, pe material (cod extern + denumire).
  const [unitati, setUnitati] = useState<Record<string, string>>({})

  const gestiuniDepozit = (gestiuni.data ?? []).filter((g) => g.tip === 'Depozit' && g.activa)
  const depozitAles = depozite.data?.find((d) => d.id === depozitId)
  const creeazaGestiuni = rezultat.gestiuni.some((g) => alegeri[g.cod]?.importa && alegeri[g.cod].gestiuneId === CREEAZA)
  const erori = rezultat.probleme.filter((p) => p.severitate === 'Eroare')
  const avertismente = rezultat.probleme.filter((p) => p.severitate === 'Avertisment')
  const alese = rezultat.gestiuni.filter((g) => alegeri[g.cod]?.importa)
  const faraUnitate = new Set(alese.flatMap((g) => g.linii.filter(faraUm)).map(cheieMaterial).filter((k) => !unitati[k]?.trim()))
  const toateBuc = () => setUnitati({ ...unitati, ...Object.fromEntries([...faraUnitate].map((k) => [k, 'buc'])) })

  const confirma = async () => {
    if (!dataSold) return
    const asocieri: AsociereGestiune[] = rezultat.gestiuni.map((g) => ({
      cod: g.cod,
      gestiuneId: alegeri[g.cod].gestiuneId === CREEAZA ? null : alegeri[g.cod].gestiuneId,
      importa: alegeri[g.cod].importa,
    }))
    try {
      const um = Object.fromEntries(alese.flatMap((g) => g.linii.filter(faraUm)).map((l) => [l.rand, unitati[cheieMaterial(l)]?.trim() ?? '']))
      const documente = await importa.mutateAsync({ fisier, data: dataSold.format('YYYY-MM-DD'), depozitId, asocieri, unitati: um })
      message.success(`Stocul a fost preluat: ${documente.map((d) => d.numar).join(', ')}.`)
      onGata()
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Descriptions size="small" bordered column={{ xs: 1, md: 2 }}
        items={[
          { key: 'f', label: 'Fișier', children: rezultat.numeFisier },
          { key: 'c', label: 'Categorie', children: rezultat.categorie ?? '—' },
          { key: 'n', label: 'Firma din fișier', children: `${rezultat.firma ?? '—'}${rezultat.cif ? ` (${rezultat.cif})` : ''}` },
          { key: 'p', label: 'Perioada', children: `${data(rezultat.perioadaDe)} – ${data(rezultat.perioadaPanaLa)}` },
          {
            key: 't', label: 'Format',
            children: rezultat.cuValori
              ? <>Raport rapid stoc, cu prețuri: <b>{formatLei(alese.reduce((s, g) => s + g.valoare, 0))}</b> în gestiunile alese</>
              : 'Balanță de stoc, doar cantități (valoare 0)',
          },
        ]} />
      {rezultat.altaFirma && <Alert type="error" showIcon message="CUI-ul din fișier nu este al firmei active. Verifică firma selectată în antet." />}
      {erori.length > 0 && (
        <Alert type="error" showIcon message="Fișierul are erori și nu poate fi preluat"
          description={<ul style={{ margin: 0 }}>{erori.map((p, i) => <li key={i}>{p.rand ? `Rândul ${p.rand}: ` : ''}{p.mesaj}</li>)}</ul>} />
      )}
      {avertismente.length > 0 && (
        <Collapse size="small" items={[{
          key: 'a',
          label: <Typography.Text type="warning">{avertismente.length} avertismente (liniile cu stoc negativ nu se preiau)</Typography.Text>,
          children: <ul style={{ margin: 0 }}>{avertismente.map((p, i) => <li key={i}>{p.rand ? `Rândul ${p.rand}: ` : ''}{p.mesaj}</li>)}</ul>,
        }]} />
      )}

      <Flex gap={12} align="center" wrap>
        <Typography.Text strong>Data soldului:</Typography.Text>
        <DatePicker value={dataSold} onChange={setDataSold} format="DD.MM.YYYY" allowClear={false} />
        <Typography.Text type="secondary">
          {rezultat.cuValori
            ? 'stocul din fișier intră în gestiuni la această dată, cu valorile din fișier (CMP-ul programului anterior)'
            : 'stocul final din fișier intră în gestiuni la această dată, cu valoare 0 (exportul nu are prețuri)'}
        </Typography.Text>
      </Flex>
      <Flex gap={12} align="center" wrap>
        <Typography.Text strong>Depozitul:</Typography.Text>
        <Select style={{ minWidth: 320 }} value={depozitId} onChange={setDepozitId} loading={depozite.isLoading}
          options={(depozite.data ?? []).filter((d) => d.activ).map((d) => ({ value: d.id, label: `${d.cod} — ${d.denumire}` }))} />
        <Typography.Text type="secondary">gestiunile din fișier sunt gestiuni ale acestui depozit; cele care lipsesc se creează aici</Typography.Text>
      </Flex>

      {rezultat.gestiuni.map((g) => {
        const alegere = alegeri[g.cod]
        const fara = g.liniiCuStoc === 0
        return (
          <Card key={g.cod} size="small"
            title={
              <Checkbox checked={alegere.importa} disabled={fara || !!g.dejaImportat}
                onChange={(e) => setAlegeri({ ...alegeri, [g.cod]: { ...alegere, importa: e.target.checked } })}>
                Gestiunea {g.cod}
              </Checkbox>
            }
            extra={
              <Space wrap>
                <Tag>{g.liniiCuStoc} {rezultat.cuValori ? 'poziții' : 'materiale'} cu stoc</Tag>
                {rezultat.cuValori && <Tag color="gold">{formatLei(g.valoare)}</Tag>}
                {g.materialeNoi > 0 && <Tag color="blue">{g.materialeNoi} noi în nomenclator</Tag>}
                {g.dejaImportat && <Tag color="green">preluată: {g.dejaImportat}</Tag>}
              </Space>
            }>
            {fara ? (
              <Typography.Text type="secondary">Nicio poziție cu stoc la sfârșitul perioadei; nu e nimic de preluat.</Typography.Text>
            ) : (
              <Space direction="vertical" style={{ width: '100%' }}>
                {!!g.alteSolduri?.length && (
                  <Alert type="warning" showIcon
                    message={`Gestiunea are deja stoc preluat pentru „${rezultat.categorie ?? 'aceeași categorie'}” din alt fișier: ${g.alteSolduri.join(', ')}.`}
                    description="Anulează-l din lista soldurilor inițiale (butonul de ștergere) înainte de a prelua acest fișier, altfel stocul se dublează." />
                )}
                <Flex gap={8} align="center" wrap>
                  <span>Gestiune în ERP:</span>
                  <Select style={{ minWidth: 380 }} value={alegere.gestiuneId} disabled={!alegere.importa} showSearch={{ optionFilterProp: 'label' }}
                    onChange={(v) => setAlegeri({ ...alegeri, [g.cod]: { ...alegere, gestiuneId: v } })}
                    options={[
                      ...(g.gestiuneId ? [] : [{ value: CREEAZA, label: `Gestiune nouă: ${g.codGestiunePropus} — Gestiunea ${g.codGestiunePropus}${depozitAles ? ` (în ${depozitAles.denumire})` : ''}` }]),
                      ...gestiuniDepozit.map((x) => ({ value: x.id, label: etichetaGestiune(x) })),
                    ]} />
                  {g.areMiscari && alegere.gestiuneId === g.gestiuneId && (
                    <Typography.Text type="warning">gestiunea are deja mișcări: cantitățile se adaugă la stocul existent</Typography.Text>
                  )}
                </Flex>
                <LiniiGestiune g={g} cuValori={rezultat.cuValori} unitati={unitati}
                  onUm={(l, um) => setUnitati({ ...unitati, [cheieMaterial(l)]: um })} />
              </Space>
            )}
          </Card>
        )
      })}

      <Flex justify="end" gap={8} align="center" wrap>
        {faraUnitate.size > 0 && (
          <>
            <Typography.Text type="warning">
              {faraUnitate.size} {faraUnitate.size === 1 ? 'material nou nu are' : 'materiale noi nu au'} U.M. (fișierul nu o conține)
            </Typography.Text>
            <Button onClick={toateBuc}>Pune „buc” la toate</Button>
          </>
        )}
        <Button onClick={onGata}>Renunță</Button>
        <Button type="primary" icon={<CloudUploadOutlined />} loading={importa.isPending}
          disabled={erori.length > 0 || alese.length === 0 || !dataSold || (creeazaGestiuni && !depozitId) || faraUnitate.size > 0} onClick={confirma}>
          Preia stocul ({alese.reduce((s, g) => s + g.liniiCuStoc, 0)} poziții)
        </Button>
      </Flex>
    </Space>
  )
}

/** Ecranul de import: alegi balanța (.xlsx exportat din programul anterior), apoi verifici și confirmi. */
function ImportBalanta({ onInchide }: { onInchide: () => void }) {
  const { message } = App.useApp()
  const interpreteaza = useInterpreteazaBalanta()
  const [fisier, setFisier] = useState<File>()
  const [rezultat, setRezultat] = useState<InterpretareBalanta>()

  const citeste = (f: File) => {
    setFisier(f)
    setRezultat(undefined)
    interpreteaza.mutate(f, { onSuccess: setRezultat, onError: (e) => { message.error((e as Error).message); setFisier(undefined) } })
    return false
  }

  const reia = () => { setFisier(undefined); setRezultat(undefined) }

  return (
    <Drawer open onClose={onInchide} size={1200} title="Preluare stoc din programul anterior">
      {!fisier || !rezultat ? (
        <Upload.Dragger accept=".xlsx" multiple={false} showUploadList={false} beforeUpload={citeste} disabled={interpreteaza.isPending}
          style={{ padding: '24px 0' }}>
          <p className="ant-upload-drag-icon"><InboxOutlined /></p>
          <p className="ant-upload-text">{interpreteaza.isPending ? 'Citesc fișierul…' : 'Trage aici raportul de stoc (.xlsx) sau apasă pentru a-l alege'}</p>
          <p className="ant-upload-hint">
            Exportul din programul de gestiune (ex. Nexus ERP), câte un fișier pe categorie (materii prime, materiale consumabile, obiecte de
            inventar): de preferință „Raport rapid stoc” (cu prețuri, stocul intră la valoarea lui), altfel „Balanță stoc” (doar cantități, valoare 0).
            Gestiunile din fișier (ex. 01-G5) sunt gestiuni ale aceluiași depozit.
          </p>
        </Upload.Dragger>
      ) : (
        <PrevizualizareBalanta key={rezultat.sha256} fisier={fisier} rezultat={rezultat} onGata={reia} />
      )}
    </Drawer>
  )
}

/** Stocurile preluate (documente SI) și importul unei balanțe noi (cu dreptul de aprobare al aprovizionării). */
export function SoldInitial({ poateAproba }: { poateAproba: boolean }) {
  const { message } = App.useApp()
  const solduri = useSolduriInitiale()
  const anulate = useSolduriInitialeAnulate()
  const anuleaza = useAnuleazaSoldInitial()
  const [importDeschis, setImportDeschis] = useState(false)

  const anuleazaSold = async (s: SoldInitialDoc) => {
    try {
      await anuleaza.mutateAsync(s.id)
      message.success(`${s.numar} a fost anulat: stocul lui a ieșit din ${s.gestiune}.`)
    } catch (e) {
      message.error((e as Error).message)
    }
  }

  return (
    <Card
      title="Stoc preluat din programul de gestiune anterior"
      extra={poateAproba && <Button type="primary" icon={<CloudUploadOutlined />} onClick={() => setImportDeschis(true)}>Importă stocul</Button>}
    >
      <Typography.Paragraph type="secondary">
        La trecerea în ERP, stocul din programul anterior intră în gestiunile depozitului printr-un document de sold inițial (SI), câte unul pe gestiune și
        fișier. Din „Raport rapid stoc” intră cu valorile lui; din „Balanță stoc” (doar cantități), cu valoare 0. Un sold preluat greșit se anulează cât timp
        materialele lui nu au alte mișcări; după aceea, diferențele se corectează prin inventar.
      </Typography.Paragraph>
      <Table<SoldInitialDoc>
        size="small"
        rowKey="id"
        loading={solduri.isLoading}
        dataSource={solduri.data}
        pagination={{ pageSize: 20, hideOnSinglePage: true }}
        expandable={{
          expandedRowRender: (s) => (
            <Table<LinieSoldInitial> size="small" rowKey="articolId" dataSource={s.detaliiLinii ?? []} pagination={{ pageSize: 20, hideOnSinglePage: true }}
              columns={[
                { title: 'Cod', dataIndex: 'articolCod', width: 110 },
                { title: 'Denumire', dataIndex: 'denumire' },
                { title: 'Cod extern', dataIndex: 'codExtern', width: 110, render: (c: string | null) => c ?? '—' },
                { title: 'Cantitate', width: 130, align: 'right', render: (_, l) => `${formatCantitate(l.cantitate)} ${l.unitateMasura}` },
                { title: 'Valoare', width: 120, align: 'right', render: (_, l) => formatLei(l.valoare) },
              ]} />
          ),
        }}
        columns={[
          { title: 'Număr', dataIndex: 'numar', width: 120 },
          { title: 'Data', dataIndex: 'data', width: 100, render: data },
          { title: 'Gestiune', dataIndex: 'gestiune' },
          { title: 'Gestiune în fișier', dataIndex: 'gestiuneExterna', width: 140 },
          { title: 'Categorie', dataIndex: 'categorie', render: (c: string | null) => c ?? '—' },
          { title: 'Poziții', dataIndex: 'linii', width: 80, align: 'right' },
          { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: (v: number) => formatLei(v) },
          { title: 'Fișier', dataIndex: 'numeFisier', ellipsis: true },
          { title: 'Importat de', dataIndex: 'importatDe', width: 150 },
          ...(poateAproba ? [{
            key: 'actiuni', width: 50,
            render: (_: unknown, s: SoldInitialDoc) => (
              <Popconfirm title={`Anulezi ${s.numar}?`} okText="Anulează soldul" cancelText="Nu" okButtonProps={{ danger: true }}
                description={`Stocul preluat (${s.linii} poziții) iese din ${s.gestiune}. Se poate doar dacă materialele n-au alte mișcări. Apare în jurnal.`}
                onConfirm={() => void anuleazaSold(s)}>
                <Tooltip title="Anulează soldul preluat greșit">
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} loading={anuleaza.isPending && anuleaza.variables === s.id} />
                </Tooltip>
              </Popconfirm>
            ),
          }] : []),
        ]}
      />
      {!!anulate.data?.length && (
        <Collapse size="small" style={{ marginTop: 16 }} items={[{
          key: 'anulate',
          label: `Solduri anulate (${anulate.data.length})`,
          children: (
            <Table size="small" rowKey="id" dataSource={anulate.data} pagination={{ pageSize: 10, hideOnSinglePage: true }}
              columns={[
                { title: 'Număr', dataIndex: 'numar', width: 120 },
                { title: 'Gestiune', dataIndex: 'gestiune' },
                { title: 'Categorie', dataIndex: 'categorie', render: (c: string | null) => c ?? '—' },
                { title: 'Poziții', dataIndex: 'linii', width: 80, align: 'right' },
                { title: 'Valoare', dataIndex: 'valoare', width: 130, align: 'right', render: (v: number) => formatLei(v) },
                { title: 'Fișier', dataIndex: 'numeFisier', ellipsis: true },
                { title: 'Anulat de', width: 220, render: (_, s) => `${s.anulatDe}, ${dayjs(s.anulatLa).format('DD.MM.YYYY HH:mm')}` },
              ]} />
          ),
        }]} />
      )}
      {importDeschis && <ImportBalanta onInchide={() => setImportDeschis(false)} />}
    </Card>
  )
}
