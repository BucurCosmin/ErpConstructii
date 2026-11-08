import { DownloadOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Spin } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import { api } from '../../api/client'

/** Originalul facturii primite: PDF-ul (scanat sau digital). Facturile doar din e-Factura (XML) nu au PDF. */
const useOriginalPdf = (facturaId?: string) =>
  useQuery({
    queryKey: ['facturi', 'original-pdf', facturaId],
    queryFn: async () => {
      try {
        return await api.getBinar(`/api/facturi/primite/${facturaId}/original/pdf`)
      } catch {
        return null
      }
    },
    enabled: !!facturaId,
    staleTime: 10 * 60_000,
  })

/** Factura afișată lângă recepție, ca gestionarul să vadă ce recepționează. */
export function PrevizualizareFactura({ facturaId, inaltime = '72vh' }: { facturaId?: string; inaltime?: string | number }) {
  const pdf = useOriginalPdf(facturaId)
  const adresa = useMemo(() => (pdf.data ? URL.createObjectURL(new Blob([pdf.data], { type: 'application/pdf' })) : undefined), [pdf.data])
  useEffect(() => () => { if (adresa) URL.revokeObjectURL(adresa) }, [adresa])

  if (pdf.isLoading) return <Flex justify="center" align="center" style={{ height: inaltime }}><Spin /></Flex>
  if (!adresa) {
    return (
      <Alert
        type="info"
        showIcon
        title="Factura nu are PDF (e din e-Factura, XML)."
        description={
          <>
            Liniile din tabel sunt exact cele din XML.{' '}
            <Button size="small" type="link" icon={<DownloadOutlined />} onClick={() => void descarcaXml(facturaId!)}>Descarcă XML-ul</Button>
          </>
        }
      />
    )
  }
  return <iframe title="Factura" src={adresa} style={{ width: '100%', height: inaltime, border: '1px solid #d9d9d9', borderRadius: 6 }} />
}

/** Originalul avizului (D-40), afișat lângă recepția pe aviz. */
export function PrevizualizareAviz({ proiectId, avizId, inaltime = '72vh' }: { proiectId?: string | null; avizId?: string; inaltime?: string | number }) {
  const original = useQuery({
    queryKey: ['aprovizionare', 'avize', 'original', avizId],
    queryFn: async () => {
      try {
        return await api.getBinar(`/api/aprovizionare/proiecte/${proiectId}/avize/${avizId}/original`)
      } catch {
        return null
      }
    },
    enabled: !!avizId && !!proiectId,
    staleTime: 10 * 60_000,
  })
  const adresa = useMemo(() => (original.data ? URL.createObjectURL(original.data) : undefined), [original.data])
  useEffect(() => () => { if (adresa) URL.revokeObjectURL(adresa) }, [adresa])
  if (original.isLoading) return <Flex justify="center" align="center" style={{ height: inaltime }}><Spin /></Flex>
  if (!adresa) return <Alert type="info" showIcon title="Originalul avizului nu poate fi afișat (de ex. e un document Word)." />
  return <iframe title="Avizul" src={adresa} style={{ width: '100%', height: inaltime, border: '1px solid #d9d9d9', borderRadius: 6 }} />
}

async function descarcaXml(facturaId: string) {
  const blob = await api.getBinar(`/api/facturi/primite/${facturaId}/original/xml`)
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'factura.xml'
  a.click()
  URL.revokeObjectURL(url)
}
