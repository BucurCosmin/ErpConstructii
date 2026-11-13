import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export type TipResursa = 'Material' | 'Manopera' | 'Utilaj' | 'Transport' | 'Echipament'

/** O resursă din nomenclatorul firmei (codul din programul de devize) și legătura ei în ERP (REQ-IMP-04, D-55). */
export interface ResursaCatalog {
  id: string
  sursa: string
  cod: string
  denumire: string
  unitateMasura: string
  tip: TipResursa
  /** Tipul a fost dedus din UM (resursă fără preț): de confirmat. */
  tipDedus: boolean
  mapata: boolean
  faraMapare: boolean
  articolStocId: string | null
  articolStocDenumire: string | null
  articolStocUnitateMasura: string | null
  /** 1 UM resursă = factor × UM stoc. */
  factorConversie: number | null
  meserie: string | null
  categorie: string | null
  utilaj: string | null
  mapataDe: string | null
  mapataLa: string | null
  /** În câte resurse ale articolelor apare, respectiv în câte versiuni de deviz. */
  utilizari: number
  versiuni: number
}

export interface ArticolStocMapare {
  id: string
  cod: string
  denumire: string
  unitateMasura: string
}

export interface MapareResursaCerere {
  tip: TipResursa
  articolStocId?: string | null
  meserie?: string | null
  categorie?: string | null
  utilaj?: string | null
  faraMapare?: boolean
  factorConversie?: number | null
}

const baza = '/api/devize/resurse'
const cheie = ['resurse-catalog'] as const

export const useResurseCatalog = (filtre: { stare: string; tip?: string; cauta?: string }) =>
  useQuery({
    queryKey: [...cheie, filtre],
    queryFn: () =>
      api.get<ResursaCatalog[]>(`${baza}?${new URLSearchParams(Object.entries(filtre).filter(([, v]) => !!v) as [string, string][]).toString()}`),
    placeholderData: keepPreviousData,
  })

/** Materialele din stoc propuse pentru resursă (după denumirea ei), sau găsite după textul căutat. */
export const usePropuneriStoc = (resursaId: string | undefined, cauta: string) =>
  useQuery({
    queryKey: [...cheie, 'propuneri', resursaId, cauta],
    queryFn: () => api.get<ArticolStocMapare[]>(`${baza}/${resursaId}/propuneri${cauta ? `?cauta=${encodeURIComponent(cauta)}` : ''}`),
    enabled: !!resursaId,
    placeholderData: keepPreviousData,
  })

export const useMapeazaResursa = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, cerere }: { id: string; cerere: MapareResursaCerere }) => api.put<ResursaCatalog>(`${baza}/${id}/mapare`, cerere),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheie }),
  })
}
