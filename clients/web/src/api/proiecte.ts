import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import { chei } from './interogari'
import type { Proiect, StareProiect, TipProiect } from './tipuri'

/** Un punct de lucru al proiectului (D-38). */
export interface Santier {
  id: string
  proiectId: string
  denumire: string
  adresa: string | null
  activ: boolean
}

/** Compartiment al firmei (aprovizionare, mecanizare, birou tehnic…), D-38. */
export interface Compartiment {
  id: string
  denumire: string
  activ: boolean
}

export interface ModificaProiect {
  tip: TipProiect
  cod: string
  denumire: string
  beneficiar: string | null
  cuiBeneficiar: string | null
  adresaSantier: string | null
  stare: StareProiect
  dataInceput: string | null
  termenContractual: string | null
}

const cheiSantiere = (proiectId?: string) => ['proiecte', proiectId, 'santiere'] as const
const cheiCompartimente = ['compartimente'] as const

export const useSantiere = (proiectId?: string, toate = false) =>
  useQuery({
    queryKey: [...cheiSantiere(proiectId), toate],
    queryFn: () => api.get<Santier[]>(`/api/proiecte/${proiectId}/santiere${toate ? '?toate=true' : ''}`),
    enabled: !!proiectId,
  })

export const useCompartimente = (toate = false) =>
  useQuery({ queryKey: [...cheiCompartimente, toate], queryFn: () => api.get<Compartiment[]>(`/api/proiecte/compartimente${toate ? '?toate=true' : ''}`) })

/** Editarea completă a proiectului (administratorul firmei). */
export const useModificaProiect = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, date }: { id: string; date: ModificaProiect }) => api.put<Proiect>(`/api/proiecte/${id}`, date),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.proiecte }),
  })
}

export const useSalveazaSantier = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id, date }: { proiectId: string; id?: string; date: { denumire: string; adresa: string | null; activ: boolean } }) =>
      id ? api.put<Santier>(`/api/proiecte/${proiectId}/santiere/${id}`, date) : api.post<Santier>(`/api/proiecte/${proiectId}/santiere`, date),
    onSuccess: (_, v) => qc.invalidateQueries({ queryKey: cheiSantiere(v.proiectId) }),
  })
}

/** Șterge un șantier pus din greșeală; cu necesare emise pe el, `mutaPe` = șantierul pe care se mută (altfel 409 cu `documente`). */
export const useStergeSantier = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id, mutaPe }: { proiectId: string; id: string; mutaPe?: string }) =>
      api.delete<void>(`/api/proiecte/${proiectId}/santiere/${id}${mutaPe ? `?mutaPe=${mutaPe}` : ''}`),
    onSuccess: (_, v) => {
      void qc.invalidateQueries({ queryKey: cheiSantiere(v.proiectId) })
      void qc.invalidateQueries({ queryKey: ['aprovizionare'] })
    },
  })
}

export const useSalveazaCompartiment = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, date }: { id?: string; date: { denumire: string; activ: boolean } }) =>
      id ? api.put<Compartiment>(`/api/proiecte/compartimente/${id}`, date) : api.post<Compartiment>('/api/proiecte/compartimente', date),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiCompartimente }),
  })
}

/** Proiectele arhivate (D-39); lista obișnuită (`useProiecte`) le ascunde. */
export const useProiecteArhivate = (activ = true) =>
  useQuery({ queryKey: [...chei.proiecte, 'arhivate'], queryFn: () => api.get<Proiect[]>('/api/proiecte?arhivate=true'), enabled: activ })

export const useArhiveazaProiect = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<Proiect>(`/api/proiecte/${id}/arhivare`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.proiecte }),
  })
}

export const useReactiveazaProiect = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<Proiect>(`/api/proiecte/${id}/reactivare`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: chei.proiecte }),
  })
}
