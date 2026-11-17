import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import { chei } from './interogari'
import type { StatisticaAchizitie } from './catalogPreturi'
import type { ValoriResurse } from './tipuri'

/** Datele unui articol editat sau adăugat manual: costul direct unitar în lei, pe resurse. */
export interface ArticolCerere {
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: Omit<ValoriResurse, 'total'>
}

/**
 * Versiunea în care s-a salvat modificarea. Devizul importat și cel aprobat nu se modifică pe loc: prima modificare
 * creează ciorna de lucru (`versiuneNoua`), iar următoarele continuă în ea (D-53).
 */
export interface RezultatEditareDeviz {
  versiuneId: string
  numar: number
  versiuneNoua: boolean
  articolId: string | null
}

export interface ArticolComparat {
  articolId: string
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: number
  valoare: number
  capitol: string
  /** Prețul / valoarea de ofertă, în moneda devizului (0 = fără ofertă). */
  pretOferta: number
  valoareOferta: number
}

export interface DiferentaVersiune {
  cheieStabila: string
  tip: 'Adaugat' | 'Modificat' | 'Sters'
  vechi: ArticolComparat | null
  nou: ArticolComparat | null
  /** La „Modificat”: simbol, denumire, um, cantitate, pret. */
  campuri: string[]
  diferentaValoare: number
  diferentaOferta: number
}

export interface ComparatieVersiuni {
  versiuneVecheId: string
  numarVechi: number
  stareVeche: 'Ciorna' | 'Aprobata' | 'Inlocuita'
  diferente: DiferentaVersiune[]
  totalFaraTvaVechi: number
  totalFaraTvaNou: number
  totalCuTvaVechi: number
  totalCuTvaNou: number
  /** Valoarea de ofertă totală (devizele de ofertă fără cost direct, D-58). */
  ofertaVeche: number
  ofertaNoua: number
  moneda: string
}

const baza = (proiectId: string) => `/api/proiecte/${proiectId}/devize`

/** Orice operație pe deviz reîncarcă lista versiunilor proiectului și structura lor (aceeași rădăcină de chei). */
function useOperatie<T, R = unknown>(proiectId: string, fn: (v: T) => Promise<R>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: chei.devize(proiectId) })
      // Bugetul urmează devizele de referință ale obiectivelor (D-59).
      void qc.invalidateQueries({ queryKey: ['proiecte', proiectId, 'buget'] })
    },
  })
}

/** Modificările față de versiunea din care a pornit ciorna (fără precedent: null). */
export const useDiferenteDeviz = (proiectId: string, versiuneId: string, activ: boolean) =>
  useQuery({
    queryKey: [...chei.deviz(proiectId, versiuneId), 'diferente'],
    queryFn: async () => (await api.get<ComparatieVersiuni | undefined>(`${baza(proiectId)}/${versiuneId}/diferente`)) ?? null,
    enabled: activ,
  })

export const useRedenumesteDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, denumire }: { versiuneId: string; denumire: string }) =>
    api.put(`${baza(proiectId)}/${versiuneId}`, { denumire }))

/** Șterge versiunea (D-59): ciorna oricând; cea aprobată / importată dacă e ultima din istorie, cu motiv. */
export const useStergeDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, motiv }: { versiuneId: string; motiv?: string }) =>
    api.delete(`${baza(proiectId)}/${versiuneId}${motiv ? `?motiv=${encodeURIComponent(motiv)}` : ''}`))

export const useAprobaDeviz = (proiectId: string) =>
  useOperatie(proiectId, (versiuneId: string) => api.post(`${baza(proiectId)}/${versiuneId}/aprobare`, {}))

/** „Modifică”: ciorna de lucru pornită din versiune (aceleași articole și chei stabile); dacă există deja, aceea. */
export const useVersiuneNouaDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, denumire }: { versiuneId: string; denumire?: string }) =>
    api.post<{ id: string; numar: number }>(`${baza(proiectId)}/${versiuneId}/versiune-noua`, { denumire: denumire ?? null }))

export const useModificaArticolDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, articolId, date }: { versiuneId: string; articolId: string; date: ArticolCerere }) =>
    api.put<RezultatEditareDeviz>(`${baza(proiectId)}/${versiuneId}/articole/${articolId}`, date))

export const useAdaugaArticolDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, capitolId, date }: { versiuneId: string; capitolId: string; date: ArticolCerere }) =>
    api.post<RezultatEditareDeviz>(`${baza(proiectId)}/${versiuneId}/capitole/${capitolId}/articole`, date))

export const useStergeArticolDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, articolId }: { versiuneId: string; articolId: string }) =>
    api.delete<RezultatEditareDeviz>(`${baza(proiectId)}/${versiuneId}/articole/${articolId}`))

export type PoliticaPret = 'MedieInterval' | 'Ultimul' | 'MinimInterval'
export type StarePropunere = 'Nelegata' | 'FaraAchizitii' | 'FaraAchizitiiRecente' | 'Egal' | 'Diferit'

/** O resursă a devizului: prețul din deviz față de prețul de achiziție (recepții = prețul facturilor), D-55. */
export interface PropunerePret {
  cod: string
  denumire: string
  unitateMasura: string
  tip: 'Material' | 'Manopera' | 'Utilaj' | 'Transport' | 'Echipament'
  cantitateDeviz: number
  pretDeviz: number
  preturiDiferiteInDeviz: boolean
  articole: number
  stare: StarePropunere
  pretPropus: number | null
  /** (propus − deviz) / deviz */
  diferenta: number | null
  /** Schimbarea costului direct la prețul propus. */
  impact: number
  achizitii: StatisticaAchizitie | null
}

export interface RaportPreturiAchizitie {
  resurse: PropunerePret[]
  impactTotal: number
  costDirectActual: number
  articoleModificateManual: string[]
}

export const useDiferentePreturi = (proiectId: string, versiuneId: string, politica: PoliticaPret, activ: boolean) =>
  useQuery({
    queryKey: [...chei.deviz(proiectId, versiuneId), 'preturi-achizitie', politica],
    queryFn: () => api.get<RaportPreturiAchizitie>(`${baza(proiectId)}/${versiuneId}/preturi-achizitie?politica=${politica}`),
    enabled: activ,
  })

/** Aplică prețurile alese în ciorna de lucru (creată la nevoie, D-53). */
export const useAplicaPreturi = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, preturi }: { versiuneId: string; preturi: { cod: string; pret: number }[] }) =>
    api.post<{ versiuneId: string; numar: number; versiuneNoua: boolean; articoleModificate: number }>(
      `${baza(proiectId)}/${versiuneId}/preturi-achizitie`, { preturi }))

// ---------------------------------------------------------------- obiectivele proiectului (D-59)

export interface Obiectiv {
  id: string
  denumire: string
  descriere: string | null
  ordine: number
  versiuni: number
  /** Versiunea după care se urmăresc bugetul și consumurile obiectivului. */
  referinta: { versiuneId: string; tip: string; numar: number; denumire: string; stare: string } | null
}

export const useObiective = (proiectId: string) =>
  useQuery({ queryKey: [...chei.devize(proiectId), 'obiective'], queryFn: () => api.get<Obiectiv[]>(`${baza(proiectId)}/obiective`) })

export const useCreeazaObiectiv = (proiectId: string) =>
  useOperatie(proiectId, (c: { denumire: string; descriere?: string | null }) => api.post<Obiectiv>(`${baza(proiectId)}/obiective`, c))

export const useModificaObiectiv = (proiectId: string) =>
  useOperatie(proiectId, ({ id, ...c }: { id: string; denumire: string; descriere?: string | null }) => api.put(`${baza(proiectId)}/obiective/${id}`, c))

export const useStergeObiectiv = (proiectId: string) =>
  useOperatie(proiectId, ({ id, motiv }: { id: string; motiv?: string }) =>
    api.delete(`${baza(proiectId)}/obiective/${id}${motiv ? `?motiv=${encodeURIComponent(motiv)}` : ''}`))

/** Mută versiunea (cu toată istoria ei) la alt obiectiv. */
export const useMutaDeviz = (proiectId: string) =>
  useOperatie(proiectId, ({ versiuneId, obiectivId, motiv }: { versiuneId: string; obiectivId: string; motiv?: string }) =>
    api.put(`${baza(proiectId)}/${versiuneId}/obiectiv`, { obiectivId, motiv }))
