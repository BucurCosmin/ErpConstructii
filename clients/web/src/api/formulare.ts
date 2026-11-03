import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface CampFormularApi {
  nume: string
  eticheta: string
  tip: 'Text' | 'Numar' | 'Suma' | 'Data'
  obligatoriu: boolean
}

export interface TipFormularApi {
  cod: string
  denumire: string
  modulErp: string
  campuri: CampFormularApi[]
  tabele: { nume: string; coloane: CampFormularApi[]; celPutinOLinie: boolean }[]
  sePoateImporta: boolean
}

export interface SablonFormularApi {
  id: string
  codTip: string
  versiune: number
  numeFisier: string
  hashSha256: string
  activ: boolean
}

export interface DateFormularApi {
  campuri: Record<string, string>
  tabele: Record<string, Record<string, string>[]>
}

export interface ProblemaFormularApi {
  severitate: 'Eroare' | 'Avertisment' | 'Informare'
  locatie: string
  mesaj: string
}

export interface PrevizualizareFormularApi {
  tip: string | null
  metadate: { codTip: string; versiuneSablon: number; documentId: string | null; numarRezervat: string | null } | null
  date: DateFormularApi
  probleme: ProblemaFormularApi[]
  poateFiImportat: boolean
}

export interface ImportFormularApi {
  id: string
  codTip: string
  numeFisier: string
  hashSha256: string
  date: DateFormularApi
  stare: string
  creatLa: string
}

export const cheiFormulare = {
  tipuri: ['formulare', 'tipuri'] as const,
  sabloane: ['formulare', 'sabloane'] as const,
  importuri: ['formulare', 'importuri'] as const,
}

export const useTipuriFormular = () =>
  useQuery({
    queryKey: cheiFormulare.tipuri,
    queryFn: () => api.get<TipFormularApi[]>('/api/formulare/tipuri'),
  })

export const useSabloaneFormular = () =>
  useQuery({
    queryKey: cheiFormulare.sabloane,
    queryFn: () => api.get<SablonFormularApi[]>('/api/formulare/sabloane'),
  })

export const useIncarcaSablon = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ codTip, fisier }: { codTip: string; fisier: File }) =>
      api.postFisier<SablonFormularApi>(`/api/formulare/sabloane?codTip=${encodeURIComponent(codTip)}`, fisier),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFormulare.sabloane }),
  })
}

export const useActiveazaSablon = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/api/formulare/sabloane/${id}/activare`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFormulare.sabloane }),
  })
}

export const usePrevizualizeazaFormular = () =>
  useMutation({
    mutationFn: (fisier: File) =>
      api.postFisier<PrevizualizareFormularApi>('/api/formulare/import/previzualizare', fisier),
  })

export const useImportaFormular = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ codTip, fisier, date }: { codTip: string; fisier: File; date: DateFormularApi }) =>
      api.postFisier<ImportFormularApi>('/api/formulare/import', fisier, {
        codTip,
        dateJson: JSON.stringify(date),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiFormulare.importuri }),
  })
}

export const useImporturiFormular = () =>
  useQuery({
    queryKey: cheiFormulare.importuri,
    queryFn: () => api.get<ImportFormularApi[]>('/api/formulare/import'),
  })

export async function descarcaSablon(id: string) {
  return api.getBinar(`/api/formulare/sabloane/${id}/fisier`)
}
