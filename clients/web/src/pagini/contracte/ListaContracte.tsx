import { PlusOutlined, WarningOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Flex, Input, Segmented, Select, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { stariContract, tipuriContract, useContracte } from '../../api/contracte'
import type { Contract } from '../../api/contracte'
import { usePermisiuni, useProiecte } from '../../api/interogari'
import { AntetPagina } from '../../layout/AntetPagina'
import { etichetaProiect, formatData, formatLei } from '../../util/format'
import { DetaliiContract, ProgresContract } from './DetaliiContract'
import { EditorContract } from './EditorContract'

/**
 * Contractele cu subantreprenori și parteneri (D-41), pe un proiect (tab-ul din fișa proiectului) sau pe toate: valoarea
 * actualizată, cât s-a facturat, restul, garanția reținută și termenul.
 */
export function ListaContracte({ proiectId }: { proiectId?: string }) {
  const permisiuni = usePermisiuni()
  const are = (d: string) => permisiuni.data?.includes(`contracte.${d}`) ?? false
  const proiecte = useProiecte()
  const [proiectAles, setProiectAles] = useState<string>()
  const [stare, setStare] = useState<string>('Toate')
  const [cauta, setCauta] = useState<string>()
  const contracte = useContracte({ proiectId: proiectId ?? proiectAles, stare: stare === 'Toate' ? undefined : stare, cauta })
  const [deschis, setDeschis] = useState<Contract>()
  const [nou, setNou] = useState(false)

  const total = (contracte.data ?? []).filter((c) => c.stare !== 'Ciorna' && c.moneda === 'RON')
  return (
    <Card
      title={proiectId ? 'Contracte cu subantreprenori și parteneri' : undefined}
      extra={<Button type="primary" icon={<PlusOutlined />} disabled={!are('creare')} onClick={() => setNou(true)}>Contract nou</Button>}
    >
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        {!proiectId && (
          <Select allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="Toate proiectele" style={{ width: 260 }} value={proiectAles} onChange={setProiectAles}
            options={proiecte.data?.map((p) => ({ value: p.id, label: etichetaProiect(p) }))} />
        )}
        <Segmented value={stare} onChange={(v) => setStare(String(v))}
          options={[{ value: 'Toate', label: 'Toate' }, { value: 'Activ', label: 'Active' }, { value: 'Ciorna', label: 'Ciorne' }, { value: 'Finalizat', label: 'Finalizate' }, { value: 'Reziliat', label: 'Reziliate' }]} />
        <Input.Search allowClear placeholder="Partener, obiect sau număr" style={{ width: 280 }} onSearch={(v) => setCauta(v.trim() || undefined)} />
      </Flex>
      {contracte.error && <Alert type="error" showIcon title="Contractele nu au putut fi încărcate." description={contracte.error.message} />}
      <Table<Contract>
        rowKey="id"
        size="small"
        loading={contracte.isLoading}
        dataSource={contracte.data}
        pagination={{ pageSize: 20, hideOnSinglePage: true }}
        onRow={(c) => ({ onClick: () => setDeschis(c), style: { cursor: 'pointer' } })}
        locale={{ emptyText: 'Niciun contract. Adaugă contractele cu subantreprenorii și prestatorii: facturile lor de servicii se leagă de ele.' }}
        summary={() => total.length > 0 && (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={proiectId ? 4 : 5}><Typography.Text strong>Total contracte semnate (RON)</Typography.Text></Table.Summary.Cell>
            <Table.Summary.Cell index={1} align="right"><Typography.Text strong>{formatLei(total.reduce((s, c) => s + c.situatie.valoareActualizata, 0))}</Typography.Text></Table.Summary.Cell>
            <Table.Summary.Cell index={2} align="right"><Typography.Text strong>{formatLei(total.reduce((s, c) => s + c.situatie.facturat, 0))}</Typography.Text></Table.Summary.Cell>
            <Table.Summary.Cell index={3} />
            <Table.Summary.Cell index={4} align="right"><Typography.Text strong>{formatLei(total.reduce((s, c) => s + c.situatie.garantieRetinuta, 0))}</Typography.Text></Table.Summary.Cell>
            <Table.Summary.Cell index={5} colSpan={2} />
          </Table.Summary.Row>
        )}
        columns={[
          {
            title: 'Contract', width: 170,
            render: (_, c) => <><Typography.Text strong>{c.numar}</Typography.Text>{c.numarExtern && <div><Typography.Text type="secondary">nr. {c.numarExtern}</Typography.Text></div>}</>,
          },
          ...(proiectId ? [] : [{ title: 'Proiect', dataIndex: 'proiectCod', width: 100 }]),
          { title: 'Partener', dataIndex: 'partenerDenumire', ellipsis: true },
          {
            title: 'Obiect', ellipsis: true,
            render: (_, c) => <Tooltip title={c.obiect}><span><Tag>{tipuriContract[c.tip].split(' (')[0]}</Tag>{c.obiect}</span></Tooltip>,
          },
          { title: 'Data', dataIndex: 'dataSemnarii', width: 100, render: formatData },
          { title: 'Valoare', width: 140, align: 'right', render: (_, c) => `${formatLei(c.situatie.valoareActualizata)}${c.moneda !== 'RON' ? ` ${c.moneda}` : ''}` },
          { title: 'Facturat', width: 130, align: 'right', render: (_, c) => formatLei(c.situatie.facturat) },
          { title: '', width: 130, render: (_, c) => (c.stare === 'Ciorna' ? null : <ProgresContract c={c} mic />) },
          { title: 'Garanție reținută', width: 130, align: 'right', render: (_, c) => (c.situatie.garantieRetinuta > 0 ? formatLei(c.situatie.garantieRetinuta) : '—') },
          {
            title: 'Termen', width: 115,
            render: (_, c) => c.situatie.termenDepasit
              ? <Tooltip title="Termenul a trecut și contractul nu e închis"><Typography.Text type="danger"><WarningOutlined /> {formatData(c.situatie.termen)}</Typography.Text></Tooltip>
              : formatData(c.situatie.termen),
          },
          { title: 'Stare', width: 100, render: (_, c) => <Tag color={stariContract[c.stare].culoare}>{stariContract[c.stare].eticheta}</Tag> },
        ]}
      />
      <EditorContract deschis={nou} proiectId={proiectId} onInchide={() => setNou(false)} onSalvat={(c) => setDeschis(c)} />
      <DetaliiContract proiectId={deschis?.proiectId} id={deschis?.id} onInchide={() => setDeschis(undefined)}
        permisiuni={{ editare: are('editare'), aprobare: are('aprobare') }} />
    </Card>
  )
}

/** Pagina „Contracte”: toate contractele din proiectele accesibile. */
export function Contracte() {
  return (
    <>
      <AntetPagina titlu="Contracte" subtitlu="Contractele cu subantreprenori și parteneri: valoare, acte adiționale, facturat, garanție de bună execuție, termene." />
      <ListaContracte />
    </>
  )
}
