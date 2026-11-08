import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { CampCitit, Certitudine } from './aprovizionare'
import { api } from './client'

/** Avizele de însoțire a mărfii (D-40): marfa venită înaintea facturii. */

export interface LinieAviz {
  denumire: string
  unitateMasura: string
  cantitate: number
}

export interface Aviz {
  id: string
  numar: string
  numarExtern: string
  data: string
  proiectId: string
  proiectCod: string
  lucrare: string | null
  furnizorId: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  linii: LinieAviz[]
  observatii: string | null
  notaComandaId: string
  notaComandaNumar: string
  notaComandaGenerata: boolean
  inregistratDe: string
  inregistratLa: string
  /** Câte NIR-uri active are; `facturat` = recepțiile au primit factura. */
  receptii: number
  facturat: boolean
  facturaNumar: string | null
}

export interface NotaComandaPropusa {
  id: string
  numar: string
  numarExtern: string | null
  data: string
  proiectId: string
  proiectCod: string
  furnizorDenumire: string
}

export interface InterpretareAviz {
  numeFisier: string
  dinOcr: boolean
  numarExtern: CampCitit<string>
  data: CampCitit<string>
  furnizorDenumire: CampCitit<string>
  furnizorCui: CampCitit<string>
  furnizorGasit: { id: string; denumire: string; cui: string | null } | null
  lucrare: CampCitit<string>
  proiectPropus: { id: string; cod: string; denumire: string; scor: number; certitudine: Certitudine } | null
  referintaComanda: string | null
  notaComandaPropusa: NotaComandaPropusa | null
  linii: { rand: number; denumire: CampCitit<string>; unitateMasura: CampCitit<string>; cantitate: CampCitit<number> }[]
  probleme: { severitate: 'Informare' | 'Avertisment' | 'Eroare'; locatie: string; mesaj: string }[]
  avizExistent: string | null
  pagini: number[]
}

export interface ConfirmaAviz {
  numarExtern: string
  data: string
  lucrare: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  linii: LinieAviz[]
  observatii: string | null
  /** Nota de comandă emisă a mărfii; null = se generează una din aviz. */
  notaComandaId: string | null
}

const cheiAvize = ['aprovizionare', 'avize'] as const

export const useAvize = (filtre: { proiectId?: string; cauta?: string }) =>
  useQuery({
    queryKey: [...cheiAvize, filtre] as const,
    queryFn: () => {
      const p = new URLSearchParams()
      if (filtre.proiectId) p.set('proiectId', filtre.proiectId)
      if (filtre.cauta) p.set('cauta', filtre.cauta)
      const q = p.toString()
      return api.get<Aviz[]>(`/api/aprovizionare/avize${q ? `?${q}` : ''}`)
    },
  })

export const useInterpreteazaAviz = () =>
  useMutation({
    mutationFn: (fisier: File) => api.postFisier<InterpretareAviz>('/api/aprovizionare/avize/import/interpretare', fisier),
  })

export const useImportaAviz = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ConfirmaAviz }) =>
      api.postFisier<Aviz>(`/api/aprovizionare/proiecte/${proiectId}/avize/import`, fisier, { date: JSON.stringify(date) }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: cheiAvize })
      void qc.invalidateQueries({ queryKey: ['aprovizionare', 'note-comanda'] })
      void qc.invalidateQueries({ queryKey: ['stocuri'] })
    },
  })
}

export const useStergeAviz = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id }: { proiectId: string; id: string }) => api.delete<void>(`/api/aprovizionare/proiecte/${proiectId}/avize/${id}`),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: cheiAvize })
      void qc.invalidateQueries({ queryKey: ['aprovizionare', 'note-comanda'] })
      void qc.invalidateQueries({ queryKey: ['stocuri'] })
    },
  })
}
