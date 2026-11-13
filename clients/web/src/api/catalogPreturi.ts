import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { ValoriResurse } from './tipuri'

/** Prețul unei resurse (cod + UM) sau al unui articol (simbol + UM) pe toate devizele firmei (D-54). */
export interface StatisticaPret {
  cod: string
  unitateMasura: string
  denumire: string
  /** Tipul resursei (Material, Manopera…) sau prefixul simbolului articolului (TS, AC…). */
  categorie: string
  observatii: number
  devize: number
  proiecte: number
  minim: number
  maxim: number
  /** Media ponderată cu cantitățile. */
  medie: number
  ultimul: number
  dataUltimului: string
  cantitate: number
  /** (max − min) / min; null când minimul e zero. */
  abatere: number | null
}

export interface VersiuneCatalog {
  id: string
  proiectId: string
  tip: string
  numar: number
  denumire: string
  stare: 'Ciorna' | 'Aprobata' | 'Inlocuita'
  data: string
  /** Nu e înlocuită și nu e o ciornă de lucru: intră în catalog. */
  inVigoare: boolean
}

export interface ObservatiePret {
  versiuneId: string
  proiectId: string
  data: string
  denumire: string
  pret: number
  cantitate: number
  componente: ValoriResurse | null
}

export interface RandComparatie {
  cod: string
  unitateMasura: string
  denumire: string
  categorie: string
  /** Prețul în fiecare versiune comparată, în ordinea lor (null = lipsește). */
  preturi: (number | null)[]
  abatere: number | null
  inToate: boolean
}

export interface ComparatiePreturi {
  versiuni: VersiuneCatalog[]
  randuri: RandComparatie[]
}

/** O resursă a unui articol din deviz (extrasul de resurse pe articol). */
export interface ConsumArticol {
  id: string
  articolId: string
  cod: string
  denumire: string
  tip: 'Material' | 'Manopera' | 'Utilaj' | 'Transport' | 'Echipament'
  unitateMasura: string
  consumUnitar: number
  cantitateTotala: number
  pretUnitar: number
  /** Consum unitar × preț: contribuția la prețul unitar al articolului. */
  valoareUnitara: number
  /** Cantitate totală × preț. */
  valoare: number
  resursaCatalogId: string | null
}

const baza = '/api/devize/catalog'
const qs = (p: Record<string, string | boolean | undefined>) =>
  new URLSearchParams(Object.entries(p).filter(([, v]) => v !== undefined && v !== '' && v !== false).map(([k, v]) => [k, String(v)])).toString()

export const useResurseArticole = (proiectId: string, versiuneId: string) =>
  useQuery({
    queryKey: ['proiecte', proiectId, 'devize', versiuneId, 'resurse-articole'],
    queryFn: () => api.get<ConsumArticol[]>(`/api/proiecte/${proiectId}/devize/${versiuneId}/resurse-articole`),
  })

export const useVersiuniCatalog = () =>
  useQuery({ queryKey: ['catalog-preturi', 'versiuni'], queryFn: () => api.get<VersiuneCatalog[]>(`${baza}/versiuni`) })

export const useCatalog = (ce: 'resurse' | 'articole', filtre: { cauta?: string; tip?: string; toate?: boolean }) =>
  useQuery({
    queryKey: ['catalog-preturi', ce, filtre],
    queryFn: () => api.get<StatisticaPret[]>(`${baza}/${ce}?${qs(filtre)}`),
    placeholderData: keepPreviousData,
  })

export const usePreturi = (ce: 'resurse' | 'articole', rand: { cod: string; unitateMasura: string } | undefined, toate: boolean) =>
  useQuery({
    queryKey: ['catalog-preturi', ce, 'preturi', rand?.cod, rand?.unitateMasura, toate],
    queryFn: () =>
      api.get<ObservatiePret[]>(`${baza}/${ce}/preturi?${qs({ [ce === 'resurse' ? 'cod' : 'simbol']: rand!.cod, um: rand!.unitateMasura, toate })}`),
    enabled: !!rand,
  })

export const useComparatie = (versiuni: string[], ce: 'articole' | 'resurse') =>
  useQuery({
    queryKey: ['catalog-preturi', 'comparatie', ce, versiuni],
    queryFn: () => api.get<ComparatiePreturi>(`${baza}/comparatie?${qs({ versiuni: versiuni.join(','), ce })}`),
    enabled: versiuni.length >= 2,
    placeholderData: keepPreviousData,
  })

/** Prețurile de achiziție (recepții) ale unei resurse legate de un material din stoc, în UM-ul resursei (D-55). */
export interface StatisticaAchizitie {
  cod: string
  unitateMasura: string
  achizitii: number
  minim: number
  maxim: number
  ultimul: number
  dataUltimului: string
  furnizorUltim: string | null
  documentUltim: string
  /** Media ponderată pe ultimele luni (implicit 6); null fără achiziții în interval. */
  medieInterval: number | null
  achizitiiInInterval: number
}

export interface AchizitieResursa {
  data: string
  sursa: string
  document: string
  documentId: string | null
  furnizor: string | null
  proiectId: string | null
  pret: number
  cantitate: number
  materialStoc: string
  factor: number
}

export const useAchizitii = (activ: boolean) =>
  useQuery({
    queryKey: ['catalog-preturi', 'achizitii'],
    queryFn: () => api.get<StatisticaAchizitie[]>(`${baza}/resurse/achizitii`),
    enabled: activ,
  })

export const useDetaliiAchizitii = (rand: { cod: string; unitateMasura: string } | undefined) =>
  useQuery({
    queryKey: ['catalog-preturi', 'achizitii', rand?.cod, rand?.unitateMasura],
    queryFn: () => api.get<AchizitieResursa[]>(`${baza}/resurse/achizitii/detalii?${qs({ cod: rand!.cod, um: rand!.unitateMasura })}`),
    enabled: !!rand,
  })
