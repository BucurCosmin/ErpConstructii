import { DownloadOutlined, SearchOutlined } from '@ant-design/icons'
import { useMutation } from '@tanstack/react-query'
import { Alert, Button, Flex, Modal, Space, Table, Tag, Tooltip, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { api, EroareApi } from '../../api/client'
import { formatCantitate, formatLei } from '../../util/format'
import { descarcaFisier } from '../../util/pdf'

interface Constatare {
  cod: string
  severitate: 'Eroare' | 'Avertisment' | 'Info' | 'Risc'
  articole: string[]
  explicatie: string
  dovezi: { nume: string; valoare: string }[]
  corectura: { articol: string; cantitateVeche: number; cantitateNoua: number } | null
  impactCostDirect: number | null
  impactOferta: number | null
  amprenta: string
}

interface RezultatAnalizor {
  cod: string
  versiune: string
  constatari: Constatare[]
  limitari: string[]
}

interface Previzualizare {
  versiune: { numar: number }
  articole: { cheieStabila: string; simbol: string; codWbs: string | null; unitateMasura: string }[]
  analizori: RezultatAnalizor[]
  amprenta: string
}

interface RandConstatare extends Constatare {
  cheie: string
  analizor: string
}

interface Props {
  proiectId: string
  versiuneId: string
  permis: boolean
}

export function VerificareDeviz({ proiectId, versiuneId, permis }: Props) {
  const [deschis, setDeschis] = useState(false)
  const analiza = useMutation({
    mutationFn: () => api.post<Previzualizare>(`/api/proiecte/${proiectId}/devize/${versiuneId}/analize/previzualizare`, {}),
  })
  const pdf = useMutation({
    mutationFn: async (rezultat: Previzualizare) => {
      const blob = await api.postBinar(`/api/proiecte/${proiectId}/devize/${versiuneId}/analize/raport/pdf`, { amprenta: rezultat.amprenta })
      descarcaFisier(blob, `Verificare_deviz_${rezultat.versiune.numar}.pdf`)
    },
  })
  const dupaCheie = useMemo(() => new Map((analiza.data?.articole ?? []).map((a) => [a.cheieStabila, a])), [analiza.data])
  const rezultate: RandConstatare[] = (analiza.data?.analizori ?? []).flatMap((a) =>
    a.constatari.map((c, i) => ({ ...c, analizor: a.cod, cheie: `${a.cod}-${c.amprenta}-${i}` })))
  const ordine = { Eroare: 0, Avertisment: 1, Risc: 2, Info: 3 }
  rezultate.sort((a, b) => ordine[a.severitate] - ordine[b.severitate]
    || Number(!!b.corectura) - Number(!!a.corectura)
    || Math.abs(b.impactCostDirect ?? 0) - Math.abs(a.impactCostDirect ?? 0))

  const coloane: TableColumnsType<RandConstatare> = [
    {
      title: 'Regulă', width: 180,
      render: (_, c) => (
        <Space vertical size={2}>
          <Tag color={{ Eroare: 'red', Avertisment: 'orange', Info: 'blue', Risc: 'purple' }[c.severitate]}>{c.severitate}</Tag>
          <Typography.Text code>{c.cod}</Typography.Text>
        </Space>
      ),
    },
    {
      title: 'Constatare',
      render: (_, c) => (
        <Flex vertical gap={4}>
          <span>{c.explicatie}</span>
          {c.articole.length > 0 && (
            <Typography.Text type="secondary">
              Articole: {c.articole.map((cheie) => {
                const a = dupaCheie.get(cheie)
                return a ? `${a.simbol}${a.codWbs ? ` (${a.codWbs})` : ''}` : cheie
              }).join(', ')}
            </Typography.Text>
          )}
          {c.corectura && (
            <Typography.Text strong>
              De verificat: {dupaCheie.get(c.corectura.articol)?.simbol ?? c.corectura.articol}:
              {' '}{formatCantitate(c.corectura.cantitateVeche)} → {formatCantitate(c.corectura.cantitateNoua)}
              {' '}{dupaCheie.get(c.corectura.articol)?.unitateMasura}
            </Typography.Text>
          )}
          {c.impactCostDirect !== null && <span>Impact estimat cost direct: {formatLei(c.impactCostDirect)} lei</span>}
          {c.impactOferta !== null && <span>Impact estimat ofertă: {formatLei(c.impactOferta)} lei</span>}
          <details>
            <summary>Dovezi</summary>
            {c.dovezi.map((d, i) => <div key={`${d.nume}-${i}`}><Typography.Text code>{d.nume}</Typography.Text>: {d.valoare}</div>)}
          </details>
        </Flex>
      ),
    },
  ]

  const ruleaza = () => {
    analiza.reset()
    pdf.reset()
    setDeschis(true)
    analiza.mutate()
  }

  return (
    <>
      <Tooltip title={!permis ? 'Este necesar dreptul de acces la prețurile de cost.' : undefined}>
        <Button icon={<SearchOutlined />} disabled={!permis} onClick={ruleaza}>Verifică devizul</Button>
      </Tooltip>
      <Modal
        title="Verificare experimentală a devizului"
        open={deschis}
        onCancel={() => { setDeschis(false); analiza.reset(); pdf.reset() }}
        footer={
          <Space>
            <Button icon={<DownloadOutlined />} disabled={!analiza.data || analiza.isPending} loading={pdf.isPending}
              onClick={() => { if (analiza.data) pdf.mutate(analiza.data) }}>Descarcă PDF</Button>
            <Button onClick={() => { setDeschis(false); analiza.reset(); pdf.reset() }}>Închide</Button>
          </Space>
        }
        width={1100}
      >
        <Flex vertical gap={12}>
          <Alert type="warning" showIcon title="Rezultatele sunt suspiciuni, nu greșeli confirmate."
            description="Verificarea este parțială și nu este validată pentru producție. Nu modifică devizul și nu blochează aprobarea. Raportul PDF se descarcă pe dispozitiv, fără a fi salvat pe server." />
          {analiza.isError && <Alert type="error" showIcon title={(analiza.error as Error).message} />}
          {pdf.isError && <Alert type="error" showIcon title={pdf.error instanceof EroareApi && pdf.error.status === 409
            ? 'Devizul sau rezultatele s-au schimbat. Rulează din nou verificarea înainte de descărcarea PDF-ului.'
            : (pdf.error as Error).message} />}
          {analiza.data && (
            <>
              <Typography.Text type="secondary">
                Analizori rulați: {analiza.data.analizori.map((a) => `${a.cod} v${a.versiune}`).join(', ')}.
                {' '}Rezultatul reflectă datele de la momentul rulării.
              </Typography.Text>
              {analiza.data.analizori.flatMap((a) => a.limitari.map((l, i) => (
                <Alert key={`${a.cod}-${i}`} type="info" showIcon title={`${a.cod}: ${l}`} />
              )))}
              <Typography.Text strong>{rezultate.length} constatări ale regulilor rulate</Typography.Text>
            </>
          )}
          <Table<RandConstatare>
            rowKey="cheie"
            columns={coloane}
            dataSource={rezultate}
            loading={analiza.isPending}
            size="small"
            pagination={rezultate.length > 10 ? { pageSize: 10, showSizeChanger: false } : false}
            locale={{ emptyText: analiza.isError ? 'Analiza nu a putut fi rulată.' : 'Nu au fost găsite suspiciuni de regulile rulate.' }}
          />
          {analiza.data && <Button onClick={() => { pdf.reset(); analiza.mutate() }} loading={analiza.isPending} disabled={pdf.isPending}>Rulează din nou</Button>}
        </Flex>
      </Modal>
    </>
  )
}
