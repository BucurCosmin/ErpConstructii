import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { ValoriResurse } from './tipuri'

export interface RandBuget {
  /** Null = cheltuieli generale (costuri fără articol de deviz). */
  cheie: string | null
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  capitol: string
  buget: ValoriResurse
  /** Costurile reale pe articol, pe resurse (deocamdată materialele consumate). */
  realizat: ValoriResurse
  documente: number
  /** Fals pentru „cheltuieli generale” și articolele care nu mai sunt în devizul de referință. */
  inDeviz: boolean
}

export interface RezumatBuget {
  bugetDirect: number
  bugetPeResurse: ValoriResurse
  realizatPeResurse: ValoriResurse
  consumPeArticole: number
  cheltuieliGenerale: number
  serviciiNerepartizate: number
  materialeCumparate: number
  materialeNeconsumate: number
  costuriInregistrate: number
  facturat: number
  procentCosturi: number | null
}

export interface VersiuneBuget { id: string; tip: string; numar: number; denumire: string; stare: 'Ciorna' | 'Aprobata' | 'Inlocuita' }

export interface BugetProiect {
  versiune: VersiuneBuget | null
  articole: RandBuget[]
  rezumat: RezumatBuget
  /** Devizul de referință al fiecărui obiectiv inclus (D-59). */
  referinte: { obiectivId: string; obiectiv: string; versiune: VersiuneBuget }[] | null
  obiective: { id: string; denumire: string; versiuni: number }[] | null
  /** Obiectivul ales; null = tot proiectul. */
  obiectivId: string | null
  /** Toate versiunile de deviz ale proiectului, de ales (D-59). */
  devize: (VersiuneBuget & { obiectivId: string })[] | null
  /** Devizul ales anume (în locul referinței); null = referința. */
  versiuneId: string | null
}

/** Ce buget se vede: tot proiectul, un obiectiv (devizul lui de referință) sau un deviz anume. */
export type AlegereBuget = { obiectivId?: string; versiuneId?: string }

export interface CostDocument {
  data: string
  natura: 'Consum' | 'AchizitieMateriale' | 'Serviciu'
  componenta: string | null
  document: string
  documentId: string | null
  descriere: string
  valoare: number
}

/** Bugetul tot proiectului, al unui obiectiv sau al unui deviz anume (D-59). */
export const useBugetProiect = (proiectId: string, alegere: AlegereBuget = {}) =>
  useQuery({
    queryKey: ['proiecte', proiectId, 'buget', alegere.versiuneId ? `v:${alegere.versiuneId}` : alegere.obiectivId ? `o:${alegere.obiectivId}` : 'toate'],
    queryFn: () => api.get<BugetProiect>(`/api/proiecte/${proiectId}/buget${alegere.versiuneId ? `?versiuneId=${alegere.versiuneId}`
      : alegere.obiectivId ? `?obiectivId=${alegere.obiectivId}` : ''}`),
    // La schimbarea obiectivului rămâne afișat bugetul anterior până vine cel nou (fără să dispară alegerea).
    placeholderData: (anterior) => anterior,
  })

/** Documentele cu costuri ale unui rând: cheia articolului, „generale”, „servicii” sau „materiale”. */
export const useCosturiBuget = (proiectId: string, articol: string | undefined) =>
  useQuery({
    queryKey: ['proiecte', proiectId, 'buget', 'costuri', articol],
    queryFn: () => api.get<CostDocument[]>(`/api/proiecte/${proiectId}/buget/costuri?articol=${encodeURIComponent(articol!)}`),
    enabled: !!articol,
  })
