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
  /** Suma liniilor dă totalurile facturii; altfel liniile au fost citite greșit (de verificat). */
  liniiConcordante: boolean
  /** Câte corecturi are factura (istoric). */
  corectii: number
}

export interface FiltreFacturi {
  proiectId?: string
  furnizor?: string
  /** Text liber: furnizor, CUI sau numărul facturii. */
  cauta?: string
  /** Un material din liniile facturii (fără diacritice / majuscule). */
  material?: string
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

/** Linia trimisă la corectură (cota TVA ca fracție: 0,21; TVA-ul liniei îl recalculează serverul). */
export interface LinieCorectie {
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: number
  valoareFaraTva: number
  cotaTva: number
  intraInStoc: boolean
}

export interface CorectieFacturaCerere {
  numar: string
  dataEmitere: string
  dataScadenta: string | null
  moneda: string
  furnizorDenumire: string
  furnizorCui: string | null
  proiectId: string
  linii: LinieCorectie[]
  totalFaraTva: number
  totalTva: number
  totalDePlata: number
  referinta: string | null
  observatii: string | null
  motiv: string
}

export interface StareCorectieFactura {
  /** Pozițiile liniilor intrate în stoc (NIR activ): materialul, cantitatea și prețul lor nu se mai schimbă. */
  liniiReceptionate: number[]
  corectii: { id: string; corectatLa: string; corectatDe: string; motiv: string; rezumat: string }[]
}

export const useStareCorectie = (id?: string) =>
  useQuery({ queryKey: [...cheiFacturi.primite, 'corectare', id], queryFn: () => api.get<StareCorectieFactura>(`/api/facturi/primite/${id}/corectare`), enabled: !!id })

/** Corectează datele unei facturi primite (cu motiv; istoricul se păstrează). */
export const useCorecteazaFactura = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, date }: { id: string; date: CorectieFacturaCerere }) => api.put<{ mesaj: string; rezumat: string }>(`/api/facturi/primite/${id}`, date),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFacturi.primite }),
  })
}
