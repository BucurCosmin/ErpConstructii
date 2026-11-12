import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  CreeazaProiect,
  FisiereImportDeviz,
  PrevizualizareImport,
  Profil,
  Proiect,
  StructuraDeviz,
  TipVersiuneDeviz,
  VersiuneDeviz,
} from './tipuri'

export const chei = {
  profil: ['profil'] as const,
  permisiuni: ['permisiuni'] as const,
  proiecte: ['proiecte'] as const,
  proiect: (id: string) => ['proiecte', id] as const,
  devize: (proiectId: string) => ['proiecte', proiectId, 'devize'] as const,
  deviz: (proiectId: string, versiuneId: string) => ['proiecte', proiectId, 'devize', versiuneId] as const,
}

export const useProfil = () => useQuery({ queryKey: chei.profil, queryFn: () => api.get<Profil>('/api/identitate/eu') })

export const usePermisiuni = () =>
  useQuery({ queryKey: chei.permisiuni, queryFn: () => api.get<string[]>('/api/identitate/eu/permisiuni') })

export const useProiecte = () => useQuery({ queryKey: chei.proiecte, queryFn: () => api.get<Proiect[]>('/api/proiecte') })

export const useProiect = (id: string) =>
  useQuery({ queryKey: chei.proiect(id), queryFn: () => api.get<Proiect>(`/api/proiecte/${id}`) })

export const useCreeazaProiect = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (p: CreeazaProiect) => api.post<{ id: string }>('/api/proiecte', p),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.proiecte }),
  })
}

export const useDevize = (proiectId: string) =>
  useQuery({
    queryKey: chei.devize(proiectId),
    queryFn: () => api.get<VersiuneDeviz[]>(`/api/proiecte/${proiectId}/devize`),
  })

export const useDeviz = (proiectId: string, versiuneId: string) =>
  useQuery({
    queryKey: chei.deviz(proiectId, versiuneId),
    queryFn: () => api.get<StructuraDeviz>(`/api/proiecte/${proiectId}/devize/${versiuneId}`),
  })

function formularImport(f: FisiereImportDeviz) {
  const date = new FormData()
  date.append('fisier', f.fisier)
  if (f.f3) date.append('f3', f.f3)
  if (f.curs) date.append('curs', String(f.curs))
  if (f.moneda) date.append('moneda', f.moneda)
  if (f.denumire?.trim()) date.append('denumire', f.denumire.trim())
  return date
}

export const usePrevizualizareImport = (proiectId: string) =>
  useMutation({
    mutationFn: (f: FisiereImportDeviz) =>
      api.postFormular<PrevizualizareImport>(`/api/proiecte/${proiectId}/devize/import/previzualizare`, formularImport(f)),
  })

export const useConfirmaImport = (proiectId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tip, ...f }: FisiereImportDeviz & { tip: TipVersiuneDeviz }) =>
      api.postFormular<{ id: string; numar: number }>(`/api/proiecte/${proiectId}/devize/import?tip=${tip}`, formularImport(f)),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.devize(proiectId) }),
  })
}
