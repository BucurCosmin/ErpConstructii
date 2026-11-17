import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export type StadiuActivitate = 'Neinceputa' | 'InCurs' | 'Finalizata'
export type TipActivitate = 'Activitate' | 'JalonInceput' | 'JalonSfarsit' | 'Rezumat' | 'Nivel'

export interface RezumatComparatie {
  noi: number
  sterse: number
  modificate: number
  intarziate: number
  finalizate: number
  sfarsitAnterior: string | null
  sfarsit: string | null
  decalajSfarsitProiectZile: number | null
  procentAnterior: number
  procent: number
}

export interface VersiuneGrafic {
  id: string
  numar: number
  denumire: string
  dataActualizare: string | null
  start: string | null
  sfarsit: string | null
  numeFisier: string
  dimensiune: number
  importatDe: string
  importatLa: string
  activitati: number
  finalizate: number
  inCurs: number
  critice: number
  procent: number
  costPlanificat: number
  costRealizat: number
  costRamas: number
  comparatie: RezumatComparatie | null
}

export interface Grafic {
  id: string
  denumire: string
  codSursa: string | null
  moneda: string | null
  format: string
  versiuneCurentaId: string | null
  creatDe: string
  creatLa: string
  /** Cea mai nouă prima. */
  versiuni: VersiuneGrafic[]
}

export interface TotalWbs {
  start: string | null
  sfarsit: string | null
  startPlanificat: string | null
  sfarsitPlanificat: string | null
  costPlanificat: number
  costRealizat: number
  costRamas: number
  procent: number
  activitati: number
  finalizate: number
  inCurs: number
  critice: number
}

export interface NodWbs {
  cheie: string
  cod: string
  denumire: string
  cheieParinte: string | null
  ordine: number
  total: TotalWbs
}

export interface ActivitateGrafic {
  cod: string
  denumire: string
  cheieWbs: string | null
  tip: TipActivitate
  stadiu: StadiuActivitate
  /** 0–100. */
  procent: number
  startPlanificat: string
  sfarsitPlanificat: string
  start: string | null
  sfarsit: string | null
  startReal: string | null
  sfarsitReal: string | null
  durataZile: number
  durataRamasaZile: number
  rezervaTotalaZile: number | null
  critica: boolean
  costPlanificat: number
  costRealizat: number
  costRamas: number
  /** Tipul codului de activitate → codul (ex. „RT-U1-Area” → „B5”). */
  coduri: Record<string, string>
  predecesori: number
}

export interface CodActivitate {
  tip: string
  cod: string
  denumire: string
}

export interface ContinutGrafic {
  grafic: Grafic
  versiune: VersiuneGrafic
  total: TotalWbs
  noduri: NodWbs[]
  activitati: ActivitateGrafic[]
  coduri: CodActivitate[]
}

export interface LegaturaActivitate {
  cod: string
  denumire: string
  tip: 'FS' | 'SS' | 'FF' | 'SF'
  decalajZile: number
  start: string | null
  sfarsit: string | null
  stadiu: StadiuActivitate
}

export interface ResursaActivitate {
  cod: string
  denumire: string
  tip: 'Manopera' | 'Material' | 'Utilaj' | 'Cheltuiala'
  unitateMasura: string | null
  cantitatePlanificata: number
  cantitateRealizata: number
  cantitateRamasa: number
  pretUnitar: number
  costPlanificat: number
  costRealizat: number
  costRamas: number
}

export interface DetaliuActivitate {
  activitate: ActivitateGrafic
  caleWbs: string | null
  startTarziu: string | null
  sfarsitTarziu: string | null
  calendar: string | null
  constrangere: string | null
  predecesori: LegaturaActivitate[]
  succesori: LegaturaActivitate[]
  resurse: ResursaActivitate[]
  pasi: { denumire: string; finalizat: boolean }[]
  istoric: { versiune: number; dataActualizare: string | null; procent: number; start: string | null; sfarsit: string | null; stadiu: StadiuActivitate }[]
}

export interface ProblemaImport {
  severitate: 'Informare' | 'Avertisment' | 'Eroare'
  locatie: string
  mesaj: string
}

export interface ActivitateComparata {
  cod: string
  denumire: string
  codWbs: string | null
}

export interface ModificareActivitate {
  cod: string
  denumire: string
  denumireAnterioara: string | null
  decalajStartZile: number | null
  decalajSfarsitZile: number | null
  procentAnterior: number
  procent: number
  stadiuAnterior: StadiuActivitate
  stadiu: StadiuActivitate
  codWbsAnterior: string | null
  codWbs: string | null
}

export interface ComparatieGrafic {
  noi: ActivitateComparata[]
  sterse: ActivitateComparata[]
  modificate: ModificareActivitate[]
  sfarsitAnterior: string | null
  sfarsit: string | null
  decalajSfarsitProiectZile: number | null
  procentAnterior: number
  procent: number
  finalizate: number
  intarziate: number
}

export interface PrevizualizareGrafic {
  format: string
  denumire: string
  codSursa: string | null
  moneda: string | null
  dataActualizare: string | null
  start: string | null
  sfarsit: string | null
  noduriWbs: number
  activitati: number
  finalizate: number
  inCurs: number
  critice: number
  legaturi: number
  cuResurse: number
  procent: number
  costPlanificat: number
  costRealizat: number
  costRamas: number
  probleme: ProblemaImport[]
  graficExistent: Grafic | null
  comparatie: ComparatieGrafic | null
  acelasiFisier: number | null
  poateFiConfirmat: boolean
}

export const stadii: Record<StadiuActivitate, { eticheta: string; culoare: string }> = {
  Neinceputa: { eticheta: 'neîncepută', culoare: 'default' },
  InCurs: { eticheta: 'în curs', culoare: 'processing' },
  Finalizata: { eticheta: 'finalizată', culoare: 'success' },
}

export const tipuriActivitate: Record<TipActivitate, string> = {
  Activitate: 'activitate',
  JalonInceput: 'jalon de început',
  JalonSfarsit: 'jalon de sfârșit',
  Rezumat: 'rezumat WBS',
  Nivel: 'nivel (LOE)',
}

const cheie = (proiectId: string) => ['proiecte', proiectId, 'grafice']
const baza = (proiectId: string) => `/api/proiecte/${proiectId}/grafice`

export const useGrafice = (proiectId: string) =>
  useQuery({ queryKey: cheie(proiectId), queryFn: () => api.get<Grafic[]>(baza(proiectId)) })

export const useContinutGrafic = (proiectId: string, graficId: string | undefined, versiuneId: string | undefined) =>
  useQuery({
    queryKey: [...cheie(proiectId), graficId, versiuneId],
    queryFn: () => api.get<ContinutGrafic>(`${baza(proiectId)}/${graficId}/versiuni/${versiuneId}`),
    enabled: !!graficId && !!versiuneId,
    staleTime: 5 * 60_000,
  })

export const useDetaliuActivitate = (proiectId: string, graficId: string, versiuneId: string, cod: string | undefined) =>
  useQuery({
    queryKey: [...cheie(proiectId), graficId, versiuneId, 'activitate', cod],
    queryFn: () => api.get<DetaliuActivitate>(`${baza(proiectId)}/${graficId}/versiuni/${versiuneId}/activitate?cod=${encodeURIComponent(cod!)}`),
    enabled: !!cod,
  })

export const useComparatieGrafic = (proiectId: string, graficId: string, versiuneId: string | undefined) =>
  useQuery({
    queryKey: [...cheie(proiectId), graficId, versiuneId, 'comparatie'],
    queryFn: () => api.get<ComparatieGrafic>(`${baza(proiectId)}/${graficId}/versiuni/${versiuneId}/comparatie`),
    enabled: !!versiuneId,
  })

/** Câmpurile formularului de import: graficul de actualizat (altfel cel cu același Project ID) sau grafic nou. */
const campuriImport = (o: { graficId?: string; graficNou?: boolean; denumire?: string }) => {
  const c: Record<string, string> = {}
  if (o.graficId) c.graficId = o.graficId
  if (o.graficNou) c.graficNou = 'true'
  if (o.denumire) c.denumire = o.denumire
  return c
}

export const previzualizeazaGrafic = (proiectId: string, fisier: File, o: { graficId?: string; graficNou?: boolean }) =>
  api.postFisier<PrevizualizareGrafic>(`${baza(proiectId)}/import/previzualizare`, fisier, campuriImport(o))

export const useImportGrafic = (proiectId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fisier, ...o }: { fisier: File; graficId?: string; graficNou?: boolean; denumire?: string }) =>
      api.postFisier<VersiuneGrafic>(`${baza(proiectId)}/import`, fisier, campuriImport(o)),
    onSuccess: () => void qc.invalidateQueries({ queryKey: cheie(proiectId) }),
  })
}

export const useStergeVersiuneGrafic = (proiectId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ graficId, versiuneId }: { graficId: string; versiuneId: string }) =>
      api.delete<unknown>(`${baza(proiectId)}/${graficId}/versiuni/${versiuneId}`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: cheie(proiectId) }),
  })
}

export const useRedenumesteGrafic = (proiectId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ graficId, denumire }: { graficId: string; denumire: string }) => api.put<unknown>(`${baza(proiectId)}/${graficId}`, { denumire }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: cheie(proiectId) }),
  })
}

export const fisierGrafic = (proiectId: string, graficId: string, versiuneId: string) =>
  api.getBinar(`${baza(proiectId)}/${graficId}/versiuni/${versiuneId}/fisier`)

// ---------------------------------------------------------------- costurile ERP pe grafic (D-57, etapa 2)

export type SursaDocumentCost = 'Factura' | 'BonConsum'
export type TipTinta = 'Activitate' | 'Wbs'
export type StareAlocare = 'Nealocat' | 'Partial' | 'Alocat' | 'FaraCost'

export interface CostTinta {
  lei: number
  /** Null dacă lipsește cursul BNR pentru vreun document. */
  monedaGrafic: number | null
  documente: number
  faraCurs: number
}

export interface LinieDocumentCost {
  index: number
  denumire: string
  unitateMasura: string | null
  cantitate: number
  /** Fără TVA, în moneda documentului. */
  valoare: number
  alocabila: boolean
  nota: string | null
  grupare: string | null
}

export interface DocumentCost {
  sursa: SursaDocumentCost
  id: string
  numar: string
  data: string
  partener: string | null
  moneda: string
  linii: LinieDocumentCost[]
  detalii: string | null
}

export interface AlocareLinie {
  sursa: SursaDocumentCost
  documentId: string
  linieIndex: number
  tip: TipTinta
  cheie: string
  procent: number
  linieDenumire: string | null
}

export interface PropunereAlocare {
  linieIndex: number
  tip: TipTinta
  cheie: string
  procent: number
  motiv: string
}

export interface StareDocumentCost {
  document: DocumentCost
  valoareLei: number
  alocatLei: number
  alocatMonedaGrafic: number | null
  procentAlocat: number
  stare: StareAlocare
  cursDocument: number | null
  cursGrafic: number | null
  dataCurs: string | null
  alocari: AlocareLinie[]
  propuneri: PropunereAlocare[]
  probleme: string[]
}

export interface CostAlocat {
  sursa: SursaDocumentCost
  documentId: string
  documentNumar: string
  data: string
  partener: string | null
  linieIndex: number
  linie: string
  tip: TipTinta
  cheie: string
  denumireTinta: string | null
  procent: number
  lei: number
  monedaGrafic: number | null
  cursDocument: number | null
  cursGrafic: number | null
  dataCurs: string | null
}

export interface CosturiGrafic {
  moneda: string
  versiuneId: string | null
  total: CostTinta
  faraTinta: CostTinta
  peActivitate: Record<string, CostTinta>
  peNod: Record<string, CostTinta>
  documente: StareDocumentCost[]
  alocari: CostAlocat[]
  surse: SursaDocumentCost[]
}

export interface AlocareCeruta {
  linieIndex: number
  tip: TipTinta
  cheie: string
  procent: number
}

export const surseDocument: Record<SursaDocumentCost, string> = { Factura: 'factură', BonConsum: 'bon de consum' }

export const stariAlocare: Record<StareAlocare, { eticheta: string; culoare: string }> = {
  Nealocat: { eticheta: 'nealocat', culoare: 'default' },
  Partial: { eticheta: 'parțial', culoare: 'gold' },
  Alocat: { eticheta: 'alocat', culoare: 'green' },
  FaraCost: { eticheta: 'fără cost direct', culoare: 'default' },
}

export const useCosturiGrafic = (proiectId: string, graficId: string | undefined, activ: boolean) =>
  useQuery({
    queryKey: [...cheie(proiectId), graficId, 'costuri'],
    queryFn: () => api.get<CosturiGrafic>(`${baza(proiectId)}/${graficId}/costuri`),
    enabled: !!graficId && activ,
  })

export const useAlocaDocument = (proiectId: string, graficId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (c: { sursa: SursaDocumentCost; documentId: string; alocari: AlocareCeruta[] }) =>
      api.put<StareDocumentCost>(`${baza(proiectId)}/${graficId}/costuri/alocari`, c),
    onSuccess: () => void qc.invalidateQueries({ queryKey: [...cheie(proiectId), graficId, 'costuri'] }),
  })
}

export const useAlocaInMasa = (proiectId: string, graficId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (c: { documente: { sursa: SursaDocumentCost; documentId: string }[]; tip: TipTinta; cheie: string }) =>
      api.post<number>(`${baza(proiectId)}/${graficId}/costuri/alocare-in-masa`, c),
    onSuccess: () => void qc.invalidateQueries({ queryKey: [...cheie(proiectId), graficId, 'costuri'] }),
  })
}

/** Valoarea selectorului de țintă: „Activitate\u0001cod” / „Wbs\u0001cheie”. */
export const valoareTinta = (tip: TipTinta, cheie: string) => `${tip}\u0001${cheie}`
export const dinValoareTinta = (v: string): { tip: TipTinta; cheie: string } => {
  const [tip, ...rest] = v.split('\u0001')
  return { tip: tip as TipTinta, cheie: rest.join('\u0001') }
}

// ---------------------------------------------------------------- valoarea din grafic vs costul ERP (D-57, etapa 3)

export interface RandBugetGrafic {
  adancime: number
  esteNod: boolean
  cheie: string
  cod: string
  denumire: string
  stadiu: StadiuActivitate | null
  valoarePlanificata: number
  procent: number
  valoareExecutata: number
  costErp: number
  costErpLei: number
  documente: number
  faraCurs: number
  /** Valoarea executată − costul ERP (negativ = cost peste valoarea executată). */
  abatere: number
  indiceCost: number | null
  prognoza: number
  procentConsumat: number | null
  alerta: string | null
}

export interface BugetGrafic {
  moneda: string
  grafic: string
  actualizare: number | null
  dataActualizare: string | null
  raport: { randuri: RandBugetGrafic[]; total: RandBugetGrafic; costFaraTinta: CostTinta; pragAlerta: number }
}

export const useBugetGrafic = (proiectId: string, graficId: string | undefined, prag: number, activ: boolean) =>
  useQuery({
    queryKey: [...cheie(proiectId), graficId, 'costuri', 'buget', prag],
    queryFn: () => api.get<BugetGrafic>(`${baza(proiectId)}/${graficId}/costuri/buget?prag=${prag}`),
    enabled: !!graficId && activ,
  })

export const bugetGraficExcel = (proiectId: string, graficId: string, prag: number) =>
  api.getBinar(`${baza(proiectId)}/${graficId}/costuri/buget.xlsx?prag=${prag}`)

export const bugetGraficPdf = (proiectId: string, graficId: string, prag: number, nivel: number | null, doarAlerte: boolean) =>
  api.getBinar(`${baza(proiectId)}/${graficId}/costuri/buget.pdf?prag=${prag}${nivel ? `&nivel=${nivel}` : ''}${doarAlerte ? '&doarAlerte=true' : ''}`)
