import { Link } from 'react-router'

/** Numărul facturii ca link spre Facturi, filtrată pe factura respectivă (din NIR, stocuri, proveniență, jurnal). */
export function LinkFactura({ id, numar }: { id: string | null | undefined; numar: string | null | undefined }) {
  if (!numar) return null
  if (!id) return <>{numar}</>
  return (
    <Link to={`/facturi?factura=${id}`} onClick={(e) => e.stopPropagation()} title="Deschide factura">
      {numar}
    </Link>
  )
}
