import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  CreeazaProiect,
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

export const usePrevizualizareImport = (proiectId: string) =>
  useMutation({
    mutationFn: (fisier: File) =>
      api.postFisier<PrevizualizareImport>(`/api/proiecte/${proiectId}/devize/import/previzualizare`, fisier),
  })

export const useConfirmaImport = (proiectId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fisier, tip }: { fisier: File; tip: TipVersiuneDeviz }) =>
      api.postFisier<{ id: string; numar: number }>(`/api/proiecte/${proiectId}/devize/import?tip=${tip}`, fisier),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.devize(proiectId) }),
  })
}
