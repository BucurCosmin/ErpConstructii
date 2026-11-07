import { Input, Table, Tag } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useFacturiPentruConsum } from '../../api/stocuri'
import type { FacturaPentruConsum } from '../../api/stocuri'

/**
 * Alegerea unei facturi recepționate (NIR), cu căutare după număr / furnizor; facturile proiectului sunt primele.
 * Folosit la consumul și la transferul în șantier al unei facturi întregi.
 */
export function TabelFacturiReceptionate({ proiectId, aleasa, onAlege }: {
  proiectId?: string
  aleasa?: FacturaPentruConsum
  onAlege: (f: FacturaPentruConsum) => void
}) {
  const [cauta, setCauta] = useState<string>()
  const facturi = useFacturiPentruConsum(proiectId, cauta)
  return (
    <>
      <Input.Search allowClear placeholder="Caută factura după număr sau furnizor" onSearch={(v) => setCauta(v.trim() || undefined)} style={{ marginBottom: 12 }} />
      <Table<FacturaPentruConsum>
        size="small"
        rowKey="facturaId"
        loading={facturi.isLoading}
        dataSource={facturi.data}
        pagination={{ pageSize: 6, hideOnSinglePage: true }}
        rowSelection={{ type: 'radio', selectedRowKeys: aleasa ? [aleasa.facturaId] : [], onChange: (_, r) => r[0] && onAlege(r[0]) }}
        onRow={(f) => ({ onClick: () => onAlege(f) })}
        locale={{ emptyText: 'Nicio factură recepționată (NIR) cu materiale.' }}
        columns={[
          { title: 'Factura', dataIndex: 'numar', width: 140 },
          { title: 'Data', dataIndex: 'data', width: 100, render: (d: string) => dayjs(d).format('DD.MM.YYYY') },
          { title: 'Furnizor', dataIndex: 'furnizorDenumire' },
          { title: 'Proiect', width: 140, render: (_, f) => (f.acelasiProiect ? <Tag color="green">{f.proiectCod}</Tag> : f.proiectCod) },
          { title: 'Materiale', width: 90, align: 'right', render: (_, f) => f.linii.length },
        ]}
      />
    </>
  )
}
