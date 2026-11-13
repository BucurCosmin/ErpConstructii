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

export interface BugetProiect {
  versiune: { id: string; tip: string; numar: number; denumire: string; stare: 'Ciorna' | 'Aprobata' | 'Inlocuita' } | null
  articole: RandBuget[]
  rezumat: RezumatBuget
}

export interface CostDocument {
  data: string
  natura: 'Consum' | 'AchizitieMateriale' | 'Serviciu'
  componenta: string | null
  document: string
  documentId: string | null
  descriere: string
  valoare: number
}

export const useBugetProiect = (proiectId: string) =>
  useQuery({ queryKey: ['proiecte', proiectId, 'buget'], queryFn: () => api.get<BugetProiect>(`/api/proiecte/${proiectId}/buget`) })

/** Documentele cu costuri ale unui rând: cheia articolului, „generale”, „servicii” sau „materiale”. */
export const useCosturiBuget = (proiectId: string, articol: string | undefined) =>
  useQuery({
    queryKey: ['proiecte', proiectId, 'buget', 'costuri', articol],
    queryFn: () => api.get<CostDocument[]>(`/api/proiecte/${proiectId}/buget/costuri?articol=${encodeURIComponent(articol!)}`),
    enabled: !!articol,
  })
