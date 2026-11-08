import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

/** Contractele proiectului cu subantreprenori și parteneri (D-41). */

export type TipContract = 'Subantreprenoriat' | 'PrestariServicii' | 'InchiriereUtilaje' | 'Transport' | 'ProiectareConsultanta' | 'Altele'
export type StareContract = 'Ciorna' | 'Activ' | 'Finalizat' | 'Reziliat'

export const tipuriContract: Record<TipContract, string> = {
  Subantreprenoriat: 'Subantreprenoriat (execuție lucrări)',
  PrestariServicii: 'Prestări servicii',
  InchiriereUtilaje: 'Închiriere utilaje',
  Transport: 'Transport',
  ProiectareConsultanta: 'Proiectare / consultanță',
  Altele: 'Altele',
}

export const stariContract: Record<StareContract, { eticheta: string; culoare: string }> = {
  Ciorna: { eticheta: 'Ciornă', culoare: 'default' },
  Activ: { eticheta: 'Activ', culoare: 'green' },
  Finalizat: { eticheta: 'Finalizat', culoare: 'blue' },
  Reziliat: { eticheta: 'Reziliat', culoare: 'red' },
}

export interface LinieContract {
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: number
  valoare: number
}

export interface SituatieContract {
  valoareInitiala: number
  valoareActualizata: number
  facturat: number
  rest: number
  procentFacturat: number
  garantieRetinuta: number
  depasit: boolean
  termen: string | null
  termenDepasit: boolean
}

export interface ActAditional {
  id: string
  numar: number
  numarExtern: string | null
  data: string
  obiect: string
  diferentaValoare: number
  dataSfarsitNoua: string | null
  creatDe: string
  creatLa: string
}

export interface DocumentContract {
  id: string
  numeFisier: string
  dimensiune: number
  actAditionalId: string | null
  incarcatDe: string
  incarcatLa: string
}

export interface Contract {
  id: string
  numar: string
  numarExtern: string | null
  dataSemnarii: string
  proiectId: string
  proiectCod: string
  partenerId: string
  partenerDenumire: string
  partenerCui: string | null
  tip: TipContract
  obiect: string
  moneda: string
  valoare: number
  linii: LinieContract[]
  dataInceput: string | null
  dataSfarsit: string | null
  termenPlataZile: number | null
  /** Fracție (0,05 = 5%). */
  procentGarantie: number
  avans: number
  taxareInversa: boolean
  observatii: string | null
  stare: StareContract
  creatDe: string
  creatLa: string
  activatLa: string | null
  activatDe: string | null
  inchidere: { tip: 'Finalizat' | 'Reziliat'; data: string; motiv: string | null; inchisDe: string } | null
  situatie: SituatieContract
  acte?: ActAditional[] | null
  facturi?: { id: string; numar: string; data: string; totalFaraTva: number; moneda: string }[] | null
  documente?: DocumentContract[] | null
}

export interface ContractCerere {
  numarExtern: string | null
  dataSemnarii: string
  partenerId: string | null
  partenerDenumire: string | null
  partenerCui: string | null
  tip: TipContract
  obiect: string
  moneda: string
  valoare: number
  linii: Omit<LinieContract, 'valoare'>[]
  dataInceput: string | null
  dataSfarsit: string | null
  termenPlataZile: number | null
  procentGarantie: number
  avans: number
  taxareInversa: boolean
  observatii: string | null
}

export interface PartenerContract {
  id: string
  denumire: string
  cui: string | null
  esteSubantreprenor: boolean
  esteFurnizor: boolean
}

const chei = ['contracte'] as const

export const useContracte = (filtre: { proiectId?: string; stare?: string; cauta?: string }) =>
  useQuery({
    queryKey: [...chei, 'lista', filtre],
    queryFn: () => {
      const p = new URLSearchParams()
      for (const [k, v] of Object.entries(filtre)) if (v) p.set(k, v)
      const q = p.toString()
      return api.get<Contract[]>(`/api/contracte${q ? `?${q}` : ''}`)
    },
  })

export const useContract = (proiectId?: string, id?: string) =>
  useQuery({
    queryKey: [...chei, 'detalii', id],
    queryFn: () => api.get<Contract>(`/api/contracte/proiecte/${proiectId}/${id}`),
    enabled: !!proiectId && !!id,
  })

export const useParteneriContract = () =>
  useQuery({ queryKey: [...chei, 'parteneri'], queryFn: () => api.get<PartenerContract[]>('/api/contracte/parteneri'), staleTime: 60_000 })

const useModificare = <T, R>(fn: (a: T) => Promise<R>) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: chei })
      void qc.invalidateQueries({ queryKey: ['facturi'] })
    },
  })
}

export const useSalveazaContract = () =>
  useModificare(({ proiectId, id, date }: { proiectId: string; id?: string; date: ContractCerere }) =>
    id ? api.put<Contract>(`/api/contracte/proiecte/${proiectId}/${id}`, date) : api.post<Contract>(`/api/contracte/proiecte/${proiectId}`, date))

export const useStergeContract = () =>
  useModificare(({ proiectId, id }: { proiectId: string; id: string }) => api.delete<void>(`/api/contracte/proiecte/${proiectId}/${id}`))

export const useActiveazaContract = () =>
  useModificare(({ proiectId, id }: { proiectId: string; id: string }) => api.post<Contract>(`/api/contracte/proiecte/${proiectId}/${id}/activare`, {}))

export const useAdaugaActAditional = () =>
  useModificare(({ proiectId, id, date }: {
    proiectId: string; id: string; date: { numarExtern: string | null; data: string; obiect: string; diferentaValoare: number; dataSfarsitNoua: string | null }
  }) => api.post<Contract>(`/api/contracte/proiecte/${proiectId}/${id}/acte-aditionale`, date))

export const useInchideContract = () =>
  useModificare(({ proiectId, id, date }: { proiectId: string; id: string; date: { tip: 'Finalizat' | 'Reziliat'; data: string; motiv: string | null } }) =>
    api.post<Contract>(`/api/contracte/proiecte/${proiectId}/${id}/inchidere`, date))

export const useIncarcaDocumentContract = () =>
  useModificare(({ proiectId, id, fisier, actAditionalId }: { proiectId: string; id: string; fisier: File; actAditionalId?: string | null }) =>
    api.postFisier<DocumentContract>(`/api/contracte/proiecte/${proiectId}/${id}/documente`, fisier, actAditionalId ? { actAditionalId } : undefined))
