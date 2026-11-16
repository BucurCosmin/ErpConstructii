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
  /** Cu actele adiționale propuse (nesemnate) incluse. */
  valoarePropusa: number
}

/** Un membru al asocierii care semnează contractul (ex. MGJV: MELSAM 50% + GCC 50%), D-56. */
export interface MembruAsociere {
  denumire: string
  cui: string | null
  /** Fracție (0,5 = 50%). */
  cota: number
  /** Membrul care e firma din aplicație. */
  esteFirmaActiva: boolean
}

export interface TransaAvans {
  numar: number
  /** Fracție din valoarea contractului. */
  procent: number
  descriere: string | null
}

/** Condițiile din fișa contractului; procentele sunt fracții (0,05 = 5%). */
export interface ConditiiContract {
  tipPret: string | null
  durata: string | null
  amplasament: string | null
  stareText: string | null
  dataPredareAmplasament: string | null
  dataPvrtl: string | null
  membri: MembruAsociere[]
  transeAvans: TransaAvans[]
  procentGarantieAvans: number
  procentGarantieTehnica: number
  luniGarantieTehnica: number | null
  penalitateExecutieZi: number
  plafonPenalitateExecutie: number
  penalitatePlataZi: number
  plafonPenalitatePlata: number
}

export interface Obligatie { id: string; ordine: number; denumire: string; temei: string | null; termen: string | null; dataTermen: string | null; stare: string | null; responsabil: string | null; observatii: string | null }
export type PrioritateActiune = 'Critica' | 'Ridicata' | 'Medie' | 'Normala'
export type StareActiune = 'Deschisa' | 'InLucru' | 'Inchisa'
export interface ActiuneFisa { id: string; ordine: number; descriere: string; prioritate: PrioritateActiune; stare: StareActiune; responsabilId: string | null; responsabilNume: string | null; termen: string | null; observatii: string | null }
export interface FazaExecutie { id: string; ordine: number; denumire: string; start: string | null; final: string | null; conditii: string | null; stare: string | null; observatii: string | null }
export interface RiscContract { id: string; ordine: number; element: string; prevedere: string | null; stare: string | null }
export type TipInstrument = 'GarantieAvans' | 'GarantieBunaExecutie' | 'GarantieTehnica' | 'BiletLaOrdin' | 'Altul'
export interface InstrumentGarantie {
  id: string; ordine: number; tip: TipInstrument; descriere: string | null; emitent: string | null; numar: string | null; beneficiar: string | null
  valoare: number | null; moneda: string | null; dataEmitere: string | null; dataExpirare: string | null; stare: string | null; observatii: string | null
}
export interface SursaContract { id: string; ordine: number; document: string; data: string | null; observatii: string | null }
export interface CorectieContract { id: string; motiv: string; modificari: string[]; corectatDe: string; la: string }

export interface RegistreFisa {
  obligatii: Obligatie[]
  actiuni: ActiuneFisa[]
  faze: FazaExecutie[]
  riscuri: RiscContract[]
  instrumente: InstrumentGarantie[]
  corecturi: CorectieContract[]
  surse?: SursaContract[] | null
}

export type Registru = 'obligatii' | 'actiuni' | 'faze' | 'riscuri' | 'instrumente' | 'surse'

export const tipuriInstrument: Record<TipInstrument, string> = {
  GarantieAvans: 'SGB restituire avans',
  GarantieBunaExecutie: 'SGB bună execuție',
  GarantieTehnica: 'SGB garanție tehnică',
  BiletLaOrdin: 'Bilet la ordin',
  Altul: 'Altul',
}
export const prioritati: Record<PrioritateActiune, { eticheta: string; culoare: string }> = {
  Critica: { eticheta: 'Critică', culoare: 'red' },
  Ridicata: { eticheta: 'Ridicată', culoare: 'orange' },
  Medie: { eticheta: 'Medie', culoare: 'gold' },
  Normala: { eticheta: 'Normală', culoare: 'default' },
}
export const stariActiune: Record<StareActiune, { eticheta: string; culoare: string }> = {
  Deschisa: { eticheta: 'Deschisă', culoare: 'blue' },
  InLucru: { eticheta: 'În lucru', culoare: 'gold' },
  Inchisa: { eticheta: 'Închisă', culoare: 'green' },
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
  /** Fals = act propus (nesemnat): nu schimbă valoarea / termenul. */
  semnat: boolean
  semnatDe: string | null
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
  conditii?: ConditiiContract | null
  fisa?: RegistreFisa | null
  facturiFisa?: FacturaFisa[] | null
  plati?: PlataContract[] | null
  situatieFisa?: SituatieFisa | null
}

export type TipFacturaContract = 'Avans' | 'Lucrari' | 'Livrare' | 'Altele'
export type InstrumentPlata = 'OrdinDePlata' | 'BiletLaOrdin' | 'Cec' | 'Compensare' | 'Altul'

export const tipuriFacturaContract: Record<TipFacturaContract, string> = { Avans: 'Avans', Lucrari: 'Lucrări', Livrare: 'Livrare', Altele: 'Altele' }
export const instrumentePlata: Record<InstrumentPlata, string> = {
  OrdinDePlata: 'Ordin de plată', BiletLaOrdin: 'Bilet la ordin', Cec: 'CEC', Compensare: 'Compensare', Altul: 'Altul',
}

/** O factură din fișa contractului: valorile de lucrări / avans în moneda contractului, sumele facturii în RON (D-56). */
export interface FacturaFisa {
  id: string
  ordine: number
  membru: string
  /** Factura din modulul Facturi (firma activă); null = introdusă în fișă (celălalt membru al asocierii). */
  facturaId: string | null
  numar: string
  data: string
  tip: TipFacturaContract
  transaAvans: number | null
  situatiePlata: string | null
  lunaExecutie: string | null
  valoare: number
  stornoAvans: number
  curs: number | null
  netRon: number
  tvaRon: number
  totalRon: number
  scadenta: string | null
  observatii: string | null
  /** Total RON − plăți confirmate. */
  sold: number
  /** Factura din modulul Facturi care pare a fi aceeași (rând preluat din fișa Excel, încă nelegat). */
  facturaPropusaId: string | null
  facturaPropusaNumar: string | null
  /** Numărul și suma se potrivesc: se poate lega fără verificare. */
  propunereSigura: boolean
  propunereDiferente: string | null
}

export interface PlataContract {
  id: string
  ordine: number
  facturaContractId: string | null
  data: string
  sumaRon: number
  instrument: InstrumentPlata
  numar: string | null
  scadenta: string | null
  /** Plata dovedită; biletul la ordin doar emis rămâne neconfirmat. */
  confirmata: boolean
  observatii: string | null
}

export type FacturaFisaCerere = Omit<FacturaFisa, 'id' | 'ordine' | 'sold' | 'facturaPropusaId' | 'facturaPropusaNumar' | 'propunereSigura' | 'propunereDiferente'>
export type PlataCerere = Omit<PlataContract, 'id' | 'ordine'>

export interface AvansTransa { numar: number; procent: number; contractual: number; facturat: number }

export interface SituatieMembru {
  membru: string
  cota: number
  esteFirmaActiva: boolean
  valoareActuala: number
  valoarePropusa: number
  avansContractual: number
  avansFacturat: number
  avansRecuperat: number
  avansRamas: number
  lucrariFacturate: number
  procentExecutat: number
  restDeExecutat: number
  facturatNetRon: number
  facturatTvaRon: number
  facturatTotalRon: number
  platitConfirmat: number
  emisNeconfirmat: number
  sold: number
  garantieBunaExecutie: number
  garantieAvans: number
  garantieTehnica: number
  transe: AvansTransa[]
  facturi: number
}

export type TipAlerta = 'FacturaScadenta' | 'InstrumentExpira' | 'InstrumentExpirat' | 'TermenDepasit' | 'ActPropus' | 'ActiuneDepasita'
export interface AlertaContract { tip: TipAlerta; mesaj: string; data: string | null }

export interface SituatieFisa {
  membri: SituatieMembru[]
  total: SituatieMembru
  soldFactura: Record<string, number>
  penalitatePosibila: number | null
  zileDepasire: number
  alerte: AlertaContract[]
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
  conditii?: ConditiiContract | null
  /** Obligatoriu la contractul semnat: modificarea devine corectură cu istoric. */
  motiv?: string | null
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
    proiectId: string; id: string; date: { numarExtern: string | null; data: string; obiect: string; diferentaValoare: number; dataSfarsitNoua: string | null; semnat: boolean }
  }) => api.post<Contract>(`/api/contracte/proiecte/${proiectId}/${id}/acte-aditionale`, date))

export const useInchideContract = () =>
  useModificare(({ proiectId, id, date }: { proiectId: string; id: string; date: { tip: 'Finalizat' | 'Reziliat'; data: string; motiv: string | null } }) =>
    api.post<Contract>(`/api/contracte/proiecte/${proiectId}/${id}/inchidere`, date))

export const useIncarcaDocumentContract = () =>
  useModificare(({ proiectId, id, fisier, actAditionalId }: { proiectId: string; id: string; fisier: File; actAditionalId?: string | null }) =>
    api.postFisier<DocumentContract>(`/api/contracte/proiecte/${proiectId}/${id}/documente`, fisier, actAditionalId ? { actAditionalId } : undefined))

export const useSemneazaAct = () =>
  useModificare(({ proiectId, id, actId, data }: { proiectId: string; id: string; actId: string; data: string }) =>
    api.post<void>(`/api/contracte/proiecte/${proiectId}/${id}/acte-aditionale/${actId}/semnare`, { data }))

/** Rândurile registrelor fișei: adăugare (fără randId), modificare (cu randId) sau ștergere. */
export const useRandFisa = () =>
  useModificare(({ proiectId, id, registru, randId, date, sterge }: {
    proiectId: string; id: string; registru: Registru; randId?: string; date?: Record<string, unknown>; sterge?: boolean
  }) => {
    const url = `/api/contracte/proiecte/${proiectId}/${id}/fisa/${registru}${randId ? `/${randId}` : ''}`
    return sterge ? api.delete<RegistreFisa>(url) : randId ? api.put<RegistreFisa>(url, date) : api.post<RegistreFisa>(url, date)
  })

/** Rândul propus pentru o factură legată de contract (din modulul Facturi), cu cursul dat. */
export const propunereFacturaFisa = (proiectId: string, id: string, facturaId: string, curs?: number | null) =>
  api.get<FacturaFisaCerere>(`/api/contracte/proiecte/${proiectId}/${id}/facturi-fisa/propunere?facturaId=${facturaId}${curs ? `&curs=${curs}` : ''}`)

export const useFacturaFisa = () =>
  useModificare(({ proiectId, id, randId, date, sterge }: { proiectId: string; id: string; randId?: string; date?: FacturaFisaCerere; sterge?: boolean }) => {
    const url = `/api/contracte/proiecte/${proiectId}/${id}/facturi-fisa${randId ? `/${randId}` : ''}`
    return sterge ? api.delete<unknown>(url) : randId ? api.put<unknown>(url, date) : api.post<unknown>(url, date)
  })

export const usePlataContract = () =>
  useModificare(({ proiectId, id, randId, date, sterge }: { proiectId: string; id: string; randId?: string; date?: PlataCerere; sterge?: boolean }) => {
    const url = `/api/contracte/proiecte/${proiectId}/${id}/plati${randId ? `/${randId}` : ''}`
    return sterge ? api.delete<unknown>(url) : randId ? api.put<unknown>(url, date) : api.post<unknown>(url, date)
  })

// ---------------------------------------------------------------- importul fișei de urmărire (D-56, etapa 3)

export type Certitudine = 'Sigur' | 'Nesigur' | 'Lipsa'
export interface CampCitit<T> { valoare: T | null; certitudine: Certitudine; textSursa: string | null }

/** Fișa citită din Excel, de verificat înainte de a crea contractul (ciornă). */
export interface FisaCitita {
  numarExtern: CampCitit<string>
  dataSemnarii: CampCitit<string>
  partenerDenumire: CampCitit<string>
  partenerCui: CampCitit<string>
  proiect: CampCitit<string>
  obiect: CampCitit<string>
  moneda: CampCitit<string>
  valoare: CampCitit<number>
  dataInceput: string | null
  dataSfarsit: string | null
  termenPlataZile: number | null
  procentGarantie: number
  conditii: ConditiiContract
  observatii: string | null
  acte: { numarExtern: string | null; data: string | null; obiect: string; diferenta: number; semnat: boolean }[]
  obligatii: { denumire: string; temei: string | null; termen: string | null; stare: string | null; observatii: string | null }[]
  actiuni: { descriere: string; prioritate: PrioritateActiune; stare: StareActiune; observatii: string | null }[]
  faze: { denumire: string; start: string | null; final: string | null; conditii: string | null; stare: string | null }[]
  riscuri: { element: string; prevedere: string | null; stare: string | null }[]
  instrumente: { tip: TipInstrument; descriere: string | null; emitent: string | null; numar: string | null; beneficiar: string | null; valoare: number | null; moneda: string | null; dataEmitere: string | null; dataExpirare: string | null }[]
  surse: { document: string; data: string | null; observatii: string | null }[]
  facturi: FacturaFisaCerere[]
  plati: { numarFactura: string; plata: Omit<PlataContract, 'id' | 'ordine'> }[]
  note: string[]
}

export interface ImportFisaCerere {
  numarExtern: string | null
  dataSemnarii: string
  partenerId: string | null
  partenerDenumire: string | null
  partenerCui: string | null
  tip: TipContract
  obiect: string
  moneda: string
  valoare: number
  membri: MembruAsociere[] | null
}

export const analizeazaFisaContract = (fisier: File) => api.postFisier<FisaCitita>('/api/contracte/import/analiza', fisier)

export const useImportFisaContract = () =>
  useModificare(({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ImportFisaCerere }) => {
    const f = new FormData()
    f.append('fisier', fisier)
    f.append('date', JSON.stringify(date))
    return api.postFormular<{ id: string; numar: string; avertismente: string[] }>(`/api/contracte/proiecte/${proiectId}/import`, f)
  })

/** Leagă rândul din fișă de factura din modulul Facturi (facturaId null = dezleagă). */
export const useLeagaFacturaFisa = () =>
  useModificare(({ proiectId, id, randId, facturaId }: { proiectId: string; id: string; randId: string; facturaId: string | null }) =>
    api.post<unknown>(`/api/contracte/proiecte/${proiectId}/${id}/facturi-fisa/${randId}/legatura`, { facturaId }))

/** Leagă toate rândurile din fișă care au o factură sigur potrivită în modulul Facturi; întoarce câte. */
export const useLeagaToateFacturileFisa = () =>
  useModificare(({ proiectId, id }: { proiectId: string; id: string }) =>
    api.post<number>(`/api/contracte/proiecte/${proiectId}/${id}/facturi-fisa/legare-automata`, {}))
