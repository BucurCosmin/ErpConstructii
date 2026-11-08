import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface Gestiune {
  id: string
  cod: string
  denumire: string
  tip: 'Depozit' | 'Santier'
  proiectId: string | null
  activa: boolean
}

export interface ArticolStoc {
  id: string
  cod: string
  denumire: string
  unitateMasura: string
  activ: boolean
}

/** Rând de sold; `cmp` și `valoare` sunt null fără dreptul „prețuri de cost”. */
export interface SoldStoc {
  gestiuneId: string
  gestiuneDenumire: string
  gestiuneTip: 'Depozit' | 'Santier'
  proiectId: string | null
  articolId: string
  articolCod: string
  denumire: string
  unitateMasura: string
  cantitate: number
  cmp: number | null
  valoare: number | null
}

export interface MiscareStoc {
  id: string
  data: string
  tip: 'IntrareNir' | 'IesireTransfer' | 'IntrareTransfer' | 'IesireConsum' | 'IesireStornoNir' | 'PlusInventar' | 'MinusInventar'
  documentNumar: string
  documentId: string
  cantitate: number
  valoare: number | null
}

export interface FacturaDeReceptionat {
  facturaId: string
  numar: string
  data: string
  furnizorDenumire: string
  proiectCod: string
  linii: number
  valoare: number | null
  /** Liniile încă de recepționat (parțial sau deloc). */
  liniiDeschise: number
  receptionataPartial: boolean
  /** Materialele care au potrivit filtrul „material”. */
  materialeGasite?: string[] | null
  /** „Aviz” = marfă venită doar cu aviz (D-40); `facturaId` e atunci Id-ul avizului. */
  tip: 'Factura' | 'Aviz'
  notaComanda: string | null
  /** La factură: NIR-uri pe avizele notei ei de comandă, încă fără factură (de legat, nu de recepționat din nou). */
  avizeDeFacturat: number
}

export interface PropunereReceptie {
  facturaId: string
  numar: string
  data: string
  furnizorId: string | null
  furnizorDenumire: string
  proiectCod: string
  linii: {
    indexFactura: number
    denumire: string
    unitateMasura: string
    cantitate: number
    pretUnitar: number | null
    valoare: number | null
    articol: { id: string; cod: string; denumire: string; unitateMasura: string; sursa: 'AliasFurnizor' | 'Alias' | 'Denumire' } | null
    /** Primit prin NIR-urile anterioare. */
    receptionat: number
    /** De recepționat. */
    ramas: number
    /** Linia nu mai așteaptă nimic (primită complet sau închisă). */
    inchisa: boolean
  }[]
  tip: 'Factura' | 'Aviz'
  notaComanda: string | null
  avizeDeFacturat: number
  proiectId: string | null
}

export interface ReceptieCerere {
  facturaId: string
  gestiuneId: string
  data: string
  linii: { indexFactura: number; articolId: string | null; articolNouDenumire: string | null; articolNouUm: string | null; cantitateReceptionata: number; observatii: string | null
    /** Restul liniei vine mai târziu; altfel linia se închide cu ce s-a primit acum. */
    restulVine: boolean }[]
  observatii: string | null
  /** Recepția pe aviz (D-40): `facturaId` se ignoră, iar `indexFactura` e poziția pe aviz. */
  avizId?: string | null
}

export interface Nir {
  id: string
  numar: string
  data: string
  gestiune: string
  /** Factura recepționată; la NIR-ul pe aviz, cea care l-a acoperit (null până vine). */
  facturaId: string | null
  facturaNumar: string | null
  furnizorDenumire: string
  proiectCod: string
  linii: { articolCod: string; denumire: string; unitateMasura: string; cantitateFactura: number; cantitateReceptionata: number; pretUnitar: number | null; valoare: number | null; observatii: string | null }[]
  valoare: number | null
  areDiferente: boolean
  receptionatDe: string
  observatii: string | null
  storno: { data: string; motiv: string; stornatDe: string } | null
  facturaAnulata: boolean
  /** Materialele care au potrivit filtrul „material”. */
  materialeGasite?: string[] | null
  /** Câte corecturi are NIR-ul (D-35). */
  corectii: number
  avizId: string | null
  avizNumar: string | null
  /** Recepție pe aviz încă fără factură (valoare provizorie). */
  asteaptaFactura: boolean
}

/** Filtrele listelor de recepție: factura / furnizorul și materialul. */
export interface FiltreReceptii {
  cauta?: string
  material?: string
}

const parametri = (f: FiltreReceptii) => {
  const p = new URLSearchParams()
  if (f.cauta) p.set('cauta', f.cauta)
  if (f.material) p.set('material', f.material)
  const q = p.toString()
  return q ? `?${q}` : ''
}

export interface TransferStoc {
  id: string
  numar: string
  directie: 'SpreSantier' | 'Retur'
  data: string
  gestiuneSursa: string
  gestiuneDestinatie: string
  proiectId: string
  proiectCod: string
  linii: { articolCod: string; denumire: string; unitateMasura: string; cantitate: number; pretUnitar: number | null; valoare: number | null }[]
  valoare: number | null
  emisDe: string
  observatii: string | null
}

const chei = {
  toate: ['stocuri'] as const,
  gestiuni: ['stocuri', 'gestiuni'] as const,
  articole: ['stocuri', 'articole'] as const,
  sold: (f: object) => ['stocuri', 'sold', f] as const,
  deReceptionat: ['stocuri', 'receptii'] as const,
  nir: ['stocuri', 'nir'] as const,
  transferuri: ['stocuri', 'transferuri'] as const,
}

const interogare = (f: Record<string, string | undefined>) => {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(f)) if (v) p.set(k, v)
  const q = p.toString()
  return q ? `?${q}` : ''
}

export const useGestiuni = () => useQuery({ queryKey: chei.gestiuni, queryFn: () => api.get<Gestiune[]>('/api/stocuri/gestiuni') })
export const useArticoleStoc = () => useQuery({ queryKey: chei.articole, queryFn: () => api.get<ArticolStoc[]>('/api/stocuri/articole') })
export const useSoldStoc = (f: { gestiuneId?: string; tip?: string; proiectId?: string; cauta?: string }) =>
  useQuery({ queryKey: chei.sold(f), queryFn: () => api.get<SoldStoc[]>(`/api/stocuri/sold${interogare(f)}`) })
export const useFisaMagazie = (gestiuneId?: string, articolId?: string) =>
  useQuery({
    queryKey: ['stocuri', 'miscari', gestiuneId, articolId],
    queryFn: () => api.get<MiscareStoc[]>(`/api/stocuri/gestiuni/${gestiuneId}/articole/${articolId}/miscari`),
    enabled: !!gestiuneId && !!articolId,
  })
export const useFacturiDeReceptionat = (filtre: FiltreReceptii = {}) =>
  useQuery({ queryKey: [...chei.deReceptionat, filtre], queryFn: () => api.get<FacturaDeReceptionat[]>(`/api/stocuri/receptii/de-facut${parametri(filtre)}`) })
export const usePropunereReceptie = (facturaId?: string, tip: 'Factura' | 'Aviz' = 'Factura') =>
  useQuery({
    queryKey: ['stocuri', 'propunere', facturaId, tip],
    queryFn: () => api.get<PropunereReceptie>(`/api/stocuri/receptii/de-facut/${facturaId}${tip === 'Aviz' ? '?tip=Aviz' : ''}`),
    enabled: !!facturaId,
  })

/** Facturarea marfii recepționate pe aviz (D-40): NIR-urile pe avizele notei de comandă a facturii. */
export interface FacturareAviz {
  facturaId: string
  numar: string
  data: string
  furnizorDenumire: string
  notaComanda: string | null
  vedePreturi: boolean
  liniiFactura: { index: number; denumire: string; unitateMasura: string; cantitate: number; pretUnitar: number | null; ramas: number }[]
  nir: {
    nirId: string
    numar: string
    data: string
    avizNumar: string
    gestiune: string
    linii: {
      indexAviz: number
      articolCod: string
      denumire: string
      denumireAviz: string
      unitateMasura: string
      cantitate: number
      pretUnitar: number | null
      indexFacturaPropus: number | null
    }[]
  }[]
}

export interface FacturareAvizCerere {
  facturaId: string
  data: string | null
  nir: { nirId: string; linii: { indexAviz: number; indexFactura: number; pretUnitar?: number | null }[] }[]
}

export const useFacturareAviz = (facturaId?: string) =>
  useQuery({
    queryKey: ['stocuri', 'facturare-aviz', facturaId],
    queryFn: () => api.get<FacturareAviz>(`/api/stocuri/receptii/facturare/${facturaId}`),
    enabled: !!facturaId,
  })

export const useFactureazaAvize = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (c: FacturareAvizCerere) => api.post<{ mesaj: string; avertismente: string[]; nir: Nir[] }>('/api/stocuri/receptii/facturare', c),
    onSuccess: () => {
      void reincarca(qc)
      void qc.invalidateQueries({ queryKey: ['aprovizionare', 'avize'] })
    },
  })
}
/** Filtrele registrului NIR. */
export interface FiltreNir extends FiltreReceptii {
  dataDe?: string
  dataPanaLa?: string
  gestiuneId?: string
  proiectId?: string
  stare?: 'Activ' | 'Stornat' | 'CuDiferente' | 'Corectat' | 'FaraFactura'
  /** NIR-urile unei facturi (link din corectura facturii). */
  facturaId?: string
}

export const useNir = (filtre: FiltreNir = {}) =>
  useQuery({ queryKey: [...chei.nir, filtre], queryFn: () => api.get<Nir[]>(`/api/stocuri/nir${interogare({ ...filtre })}`) })
export const useTransferuri = () => useQuery({ queryKey: chei.transferuri, queryFn: () => api.get<TransferStoc[]>('/api/stocuri/transferuri') })

/** După orice operație de stoc se reîncarcă tot modulul (solduri, liste, propuneri). */
const reincarca = (qc: ReturnType<typeof useQueryClient>) => qc.invalidateQueries({ queryKey: chei.toate })

export const useReceptioneaza = () => {
  const qc = useQueryClient()
  return useMutation({ mutationFn: (c: ReceptieCerere) => api.post<Nir>('/api/stocuri/receptii', c), onSuccess: () => reincarca(qc) })
}

export const useTransfera = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, ...c }: { proiectId: string; gestiuneSursaId: string; data: string; linii: { articolId: string; cantitate: number }[]; observatii: string | null }) =>
      api.post<TransferStoc>(`/api/stocuri/proiecte/${proiectId}/transferuri`, c),
    onSuccess: () => reincarca(qc),
  })
}

export const useCreeazaDepozit = () => {
  const qc = useQueryClient()
  return useMutation({ mutationFn: (c: { cod: string; denumire: string }) => api.post<Gestiune>('/api/stocuri/gestiuni', c), onSuccess: () => reincarca(qc) })
}

export const useModificaArticol = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...c }: { id: string; denumire: string; unitateMasura: string; activ: boolean }) => api.put<void>(`/api/stocuri/articole/${id}`, c),
    onSuccess: () => reincarca(qc),
  })
}

// ---------------------------------------------------------------- consum pe articole de deviz, retur

export interface ArticolDeviz {
  cheie: string
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  obiect: string
  categorie: string
}

export interface DevizReferinta {
  versiuneId: string
  denumire: string
  tip: string
  stare: string
  articole: ArticolDeviz[]
}

export interface BonConsum {
  id: string
  numar: string
  data: string
  proiectId: string
  proiectCod: string
  linii: { articolCod: string; denumire: string; unitateMasura: string; cantitate: number; pretUnitar: number | null; valoare: number | null;
    articolDevizCheie: string | null; articolDevizSimbol: string | null; articolDevizDenumire: string | null }[]
  valoare: number | null
  emisDe: string
  observatii: string | null
}

export interface ConsumPeArticole {
  deviz: string | null
  articole: {
    cheie: string | null
    simbol: string
    denumire: string
    bugetMaterial: number | null
    consumat: number | null
    diferenta: number | null
    procentConsumat: number | null
    materiale: { cod: string; denumire: string; unitateMasura: string; cantitate: number; valoare: number | null }[]
  }[]
  totalBuget: number | null
  totalConsumat: number | null
}

export const useDevizReferinta = (proiectId?: string) =>
  useQuery({
    queryKey: ['stocuri', 'deviz', proiectId],
    queryFn: () => api.get<DevizReferinta>(`/api/stocuri/proiecte/${proiectId}/deviz`),
    enabled: !!proiectId,
    retry: false,
  })

export interface LinieFacturaConsum {
  articolId: string
  articolCod: string
  denumire: string
  unitateMasura: string
  /** Recepționat prin NIR-urile facturii. */
  cantitate: number
  inSantier: number
  depozitId: string
  depozit: string
  inDepozit: number
}

export interface FacturaPentruConsum {
  facturaId: string
  numar: string
  data: string
  furnizorDenumire: string
  proiectCod: string
  acelasiProiect: boolean
  linii: LinieFacturaConsum[]
}

/** Facturile recepționate, cu materialele lor și stocul din șantier / depozit (consumul unei facturi întregi). */
export const useFacturiPentruConsum = (proiectId?: string, cauta?: string) =>
  useQuery({
    queryKey: [...chei.toate, 'consum-facturi', proiectId, cauta],
    queryFn: () => api.get<FacturaPentruConsum[]>(`/api/stocuri/proiecte/${proiectId}/consum/facturi${cauta ? `?cauta=${encodeURIComponent(cauta)}` : ''}`),
    enabled: !!proiectId,
  })

export const useBonuriConsum = () => useQuery({ queryKey: ['stocuri', 'consumuri'], queryFn: () => api.get<BonConsum[]>('/api/stocuri/consumuri') })

export const useConsumPeArticole = (proiectId?: string) =>
  useQuery({
    queryKey: ['stocuri', 'consum-pe-articole', proiectId],
    queryFn: () => api.get<ConsumPeArticole>(`/api/stocuri/proiecte/${proiectId}/consum-pe-articole`),
    enabled: !!proiectId,
  })

export const useConsuma = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, ...c }: { proiectId: string; data: string; linii: { articolId: string; cantitate: number; articolDevizCheie: string | null }[]; observatii: string | null }) =>
      api.post<BonConsum>(`/api/stocuri/proiecte/${proiectId}/consumuri`, c),
    onSuccess: () => reincarca(qc),
  })
}

export const useReturneaza = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, ...c }: { proiectId: string; gestiuneDestinatieId: string; data: string; linii: { articolId: string; cantitate: number }[]; observatii: string | null }) =>
      api.post<TransferStoc>(`/api/stocuri/proiecte/${proiectId}/retururi`, c),
    onSuccess: () => reincarca(qc),
  })
}

// ---------------------------------------------------------------- storno NIR, inventar

export interface LinieStareCorectieNir {
  indexFactura: number
  articolCod: string
  denumire: string
  unitateMasura: string
  cantitateFactura: number
  cantitateReceptionata: number
  pretUnitar: number | null
  valoare: number | null
  observatii: string | null
  partiala: boolean
  /** Ce scrie acum pe factură (după corecturile ei); null dacă factura e anulată / linia nu mai e de stoc. */
  facturaDenumire: string | null
  facturaCantitate: number | null
  facturaPret: number | null
}

export interface StareCorectieNir {
  nirId: string
  numar: string
  stornat: boolean
  vedePreturi: boolean
  linii: LinieStareCorectieNir[]
  corectii: { id: string; data: string; corectatLa: string; corectatDe: string; motiv: string; rezumat: string; diferentaValoare: number | null }[]
}

export interface CorecteazaNirCerere {
  linii: { indexFactura: number; cantitateFactura: number; cantitateReceptionata: number; pretUnitar: number | null; observatii: string | null }[]
  motiv: string
  data: string | null
}

export const useStareCorectieNir = (id?: string) =>
  useQuery({ queryKey: [...chei.nir, 'corectare', id], queryFn: () => api.get<StareCorectieNir>(`/api/stocuri/nir/${id}/corectare`), enabled: !!id })

/** Corectează un NIR recepționat greșit; stocul primește diferențele. */
export const useCorecteazaNir = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, cerere }: { id: string; cerere: CorecteazaNirCerere }) => api.put<{ mesaj: string; rezumat: string; nir: Nir }>(`/api/stocuri/nir/${id}`, cerere),
    onSuccess: () => {
      void reincarca(qc)
      void qc.invalidateQueries({ queryKey: ['audit'] })
    },
  })
}

export const useStorneazaNir = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...c }: { id: string; data: string; motiv: string }) => api.post<Nir>(`/api/stocuri/nir/${id}/storno`, c),
    onSuccess: () => reincarca(qc),
  })
}

export interface LinieInventar {
  articolId: string
  articolCod: string
  denumire: string
  unitateMasura: string
  cantitateScriptica: number
  valoareScriptica: number | null
  cantitateFaptica: number | null
  diferenta: number | null
  pretUnitarPlus: number | null
  valoareDiferenta: number | null
}

export interface Inventar {
  id: string
  numar: string
  data: string
  gestiuneId: string
  gestiune: string
  stare: 'Deschis' | 'Finalizat'
  comisie: string | null
  linii: LinieInventar[]
  numarate: number
  valoarePlusuri: number | null
  valoareMinusuri: number | null
  deschisDe: string
  finalizatDe: string | null
}

export const useInventare = () => useQuery({ queryKey: ['stocuri', 'inventare'], queryFn: () => api.get<Inventar[]>('/api/stocuri/inventare') })

export const useDeschideInventar = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (c: { gestiuneId: string; data: string; comisie: string | null }) => api.post<Inventar>('/api/stocuri/inventare', c),
    onSuccess: () => reincarca(qc),
  })
}

export const useNumaraInventar = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, linii }: { id: string; linii: { articolId: string; cantitateFaptica: number | null; pretUnitarPlus: number | null }[] }) =>
      api.put<Inventar>(`/api/stocuri/inventare/${id}/numarare`, { linii }),
    onSuccess: () => reincarca(qc),
  })
}

export const useFinalizeazaInventar = () => {
  const qc = useQueryClient()
  return useMutation({ mutationFn: (id: string) => api.post<Inventar>(`/api/stocuri/inventare/${id}/finalizare`, {}), onSuccess: () => reincarca(qc) })
}

export const useRenuntaInventar = () => {
  const qc = useQueryClient()
  return useMutation({ mutationFn: (id: string) => api.delete<void>(`/api/stocuri/inventare/${id}`), onSuccess: () => reincarca(qc) })
}

/** O cantitate din stocul curent, cu documentul prin care a intrat (NIR + factură, sau plus la inventar). */
export interface LotStoc {
  gestiuneId: string
  gestiune: string
  gestiuneTip: 'Depozit' | 'Santier'
  articolId: string
  articolCod: string
  denumire: string
  unitateMasura: string
  cantitate: number
  origine: 'NIR' | 'Inventar' | 'Transfer'
  documentId: string
  documentNumar: string
  dataIntrare: string
  facturaId: string | null
  facturaNumar: string | null
  facturaData: string | null
  furnizorDenumire: string | null
  facturaProiectCod: string | null
  /** Prețul de intrare (NIR); null fără dreptul „prețuri de cost”. */
  pretIntrare: number | null
  valoare: number | null
  /** Avizul pe care a venit marfa (D-40): „aviz + factură” sau, fără factură, doar avizul. */
  avizNumar: string | null
}

export interface FiltreTrasabilitate {
  gestiuneId?: string
  tip?: string
  material?: string
  cauta?: string
}

/** Proveniența stocului: pe ce NIR / factură a intrat fiecare cantitate din gestiuni. */
export const useTrasabilitate = (f: FiltreTrasabilitate) =>
  useQuery({ queryKey: [...chei.toate, 'trasabilitate', f], queryFn: () => api.get<LotStoc[]>(`/api/stocuri/trasabilitate${interogare({ ...f })}`) })
