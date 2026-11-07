import { useQuery } from '@tanstack/react-query'
import { api } from './client'

export type CategorieJurnal = 'Facturi' | 'NIR' | 'Stocuri' | 'Aprovizionare'

export interface FiltreJurnal {
  categorie?: CategorieJurnal
  dataDe?: string
  dataPanaLa?: string
  utilizator?: string
  /** Numărul / denumirea documentului (conține). */
  document?: string
  entitateId?: string
  actiune?: 'Adaugat' | 'Modificat' | 'Sters'
}

export interface IntrareJurnal {
  id: string
  moment: string
  utilizator: string | null
  categorie: string
  entitate: string
  actiune: 'Adaugat' | 'Modificat' | 'Sters'
  document: string | null
  descriere: string
  entitateId: string
}

/** Jurnalul modificărilor (facturi, NIR, stocuri, aprovizionare): cine, când, ce s-a schimbat. */
export const useJurnal = (filtre: FiltreJurnal) =>
  useQuery({
    queryKey: ['audit', 'jurnal', filtre] as const,
    queryFn: () => {
      const p = new URLSearchParams()
      for (const [k, v] of Object.entries(filtre)) if (v) p.set(k, v)
      const q = p.toString()
      return api.get<IntrareJurnal[]>(`/api/audit/jurnal${q ? `?${q}` : ''}`)
    },
  })
