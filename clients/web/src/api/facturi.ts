import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { CampCitit, Certitudine } from './aprovizionare'
import { api } from './client'

export interface LinieFactura {
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: number
  valoareFaraTva: number
  cotaTva: number
  valoareTva: number
  /** Intră în stoc (recepție NIR); false pentru servicii. */
  intraInStoc?: boolean
}

export interface FacturaPrimita {
  id: string
  numar: string
  dataEmitere: string
  dataScadenta: string | null
  moneda: string
  furnizorId: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  proiectId: string
  proiectCod: string
  totalFaraTva: number
  totalTva: number
  totalDePlata: number
  referinta: string | null
  observatii: string | null
  sursa: 'EFactura' | 'Pdf' | 'Manual'
  stare: 'Inregistrata' | 'Anulata'
  esteStorno: boolean
  areXml: boolean
  arePdf: boolean
  inregistrataDeNume: string
  inregistrataLa: string
  motivAnulare: string | null
  linii: LinieFactura[]
  /** Materialele din factură care au potrivit căutarea. */
  materialeGasite?: string[] | null
}

export interface FiltreFacturi {
  proiectId?: string
  furnizor?: string
  /** Text liber: furnizor, CUI, numărul facturii sau un material din linii. */
  cauta?: string
  dataDe?: string
  dataPanaLa?: string
  stare?: FacturaPrimita['stare']
}

export interface LinieFacturaCitita {
  rand: number
  denumire: CampCitit<string>
  unitateMasura: CampCitit<string>
  cantitate: CampCitit<number>
  pretUnitar: CampCitit<number>
  valoareFaraTva: CampCitit<number>
  cotaTva: CampCitit<number>
}

export interface InterpretareFactura {
  numeFisier: string
  sursa: 'EFactura' | 'Pdf'
  dinOcr: boolean
  numar: CampCitit<string>
  dataEmitere: CampCitit<string>
  dataScadenta: CampCitit<string>
  moneda: CampCitit<string>
  furnizorDenumire: CampCitit<string>
  furnizorCui: CampCitit<string>
  cumparatorCui: CampCitit<string>
  totalFaraTva: CampCitit<number>
  totalTva: CampCitit<number>
  totalDePlata: CampCitit<number>
  linii: LinieFacturaCitita[]
  referinta: string | null
  furnizorInNomenclator: { id: string; denumire: string; cui: string | null } | null
  proiectPropus: { id: string; cod: string; denumire: string; scor: number; certitudine: Certitudine } | null
  probleme: { severitate: 'Informare' | 'Avertisment' | 'Eroare'; locatie: string; mesaj: string }[]
  facturaExistenta: string | null
  /** Ce conține fiecare pagină a PDF-ului (dosare scanate: factură, e-Factura, notă de comandă, aviz, certificate). */
  pagini: PaginaDocument[]
  /** Paginile din care s-a citit factura. */
  paginiFactura: number[]
}

export type TipPagina = 'Factura' | 'EFactura' | 'NotaComanda' | 'NecesarMateriale' | 'BonConsum' | 'Aviz' | 'Certificat' | 'Anexa' | 'Altele'

export interface PaginaDocument {
  pagina: number
  tip: TipPagina
  descriere: string
  numar: string | null
  data: string | null
}

export interface ConfirmaFactura {
  numar: string
  dataEmitere: string
  dataScadenta: string | null
  moneda: string
  furnizorDenumire: string
  furnizorCui: string | null
  adaugaFurnizorInNomenclator: boolean
  linii: Omit<LinieFactura, 'valoareTva'>[]
  totalFaraTva: number
  totalTva: number
  totalDePlata: number
  referinta: string | null
  observatii: string | null
}

export const cheiFacturi = { primite: ['facturi', 'primite'] as const }

export const useFacturiPrimite = (filtre: FiltreFacturi) =>
  useQuery({
    queryKey: [...cheiFacturi.primite, filtre] as const,
    queryFn: () => {
      const p = new URLSearchParams()
      for (const [k, v] of Object.entries(filtre)) if (v) p.set(k, v)
      const q = p.toString()
      return api.get<FacturaPrimita[]>(`/api/facturi/primite${q ? `?${q}` : ''}`)
    },
  })

/** Citește factura (XML exact, PDF cu text sau OCR) și propune datele; nu salvează. */
export const useInterpreteazaFactura = () =>
  useMutation({ mutationFn: (fisier: File) => api.postFisier<InterpretareFactura>('/api/facturi/primite/import/interpretare', fisier) })

export const useImportaFactura = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ConfirmaFactura }) =>
      api.postFisier<FacturaPrimita>(`/api/facturi/proiecte/${proiectId}/primite/import`, fisier, { date: JSON.stringify(date) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFacturi.primite }),
  })
}

export const useAnuleazaFactura = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, motiv }: { id: string; motiv: string }) => api.post<FacturaPrimita>(`/api/facturi/primite/${id}/anulare`, { motiv }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFacturi.primite }),
  })
}
