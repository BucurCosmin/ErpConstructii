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
  /** Nota de comandă a achiziției (D-40). */
  notaComandaId: string | null
  notaComandaNumar: string | null
  /** Contractul cu partenerul, la facturile de servicii (D-41). */
  contractId: string | null
  contractNumar: string | null
}

/** Contractul propus pentru o factură de servicii (D-41); cele cu furnizorul facturii sunt primele. */
export interface ContractFactura {
  id: string
  numar: string
  numarExtern: string | null
  dataSemnarii: string
  partenerDenumire: string
  tip: string
  obiect: string
  moneda: string
  valoareActualizata: number
  facturat: number
  /** Cât mai e de facturat, înaintea acestei facturi. */
  rest: number
  procentGarantie: number
  taxareInversa: boolean
  acelasiPartener: boolean
}

export const useContracteFactura = (proiectId?: string, furnizorId?: string | null, furnizor?: string) =>
  useQuery({
    queryKey: ['facturi', 'contracte', proiectId, furnizorId, furnizor],
    queryFn: () => {
      const p = new URLSearchParams({ proiectId: proiectId! })
      if (furnizorId) p.set('furnizorId', furnizorId)
      if (furnizor) p.set('furnizor', furnizor)
      return api.get<ContractFactura[]>(`/api/facturi/contracte?${p.toString()}`)
    },
    enabled: !!proiectId,
  })

/** Nota de comandă propusă pentru factură (D-40); cele de la furnizorul facturii sunt primele. */
export interface NotaComandaFactura {
  id: string
  numar: string
  numarExtern: string | null
  data: string
  proiectId: string
  proiectCod: string
  furnizorDenumire: string
  /** Avize cu marfă venită înaintea facturii. */
  avize: number
  materiale: string
  acelasiFurnizor: boolean
}

export const useNoteComandaFactura = (proiectId?: string, furnizorId?: string | null, furnizor?: string) =>
  useQuery({
    queryKey: ['facturi', 'note-comanda', proiectId, furnizorId, furnizor],
    queryFn: () => {
      const p = new URLSearchParams({ proiectId: proiectId! })
      if (furnizorId) p.set('furnizorId', furnizorId)
      if (furnizor) p.set('furnizor', furnizor)
      return api.get<NotaComandaFactura[]>(`/api/facturi/note-comanda?${p.toString()}`)
    },
    enabled: !!proiectId,
  })

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
  /** O factură anume (link din NIR, stocuri, proveniență). */
  facturaId?: string
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
  /** Obligatorie la facturile cu materiale de stoc (D-40); lipsă = singura notă a furnizorului pe proiect, dacă există. */
  notaComandaId: string | null
  /** Obligatoriu la facturile doar de servicii (D-41); lipsă = singurul contract activ cu furnizorul pe proiect, dacă există. */
  contractId: string | null
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
  /** Nota de comandă (D-40); null = rămâne cea actuală. */
  notaComandaId?: string | null
  /** Contractul (D-41); null = rămâne cel actual. */
  contractId?: string | null
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
    mutationFn: ({ id, date }: { id: string; date: CorectieFacturaCerere }) => api.put<{ mesaj: string; rezumat: string; avertismente: string[] }>(`/api/facturi/primite/${id}`, date),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFacturi.primite }),
  })
}
