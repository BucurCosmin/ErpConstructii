// Tipurile DTO ale API-ului (oglindesc record-urile C# din modulele *.Infrastructure).
// La creșterea API-ului se vor genera automat din /openapi/v1.json, partajate cu aplicația mobilă.

export type StareProiect =
  | 'Ofertare'
  | 'Contractat'
  | 'InExecutie'
  | 'ReceptieLaTerminare'
  | 'PerioadaGarantie'
  | 'Inchis'
  | 'Pierdut'

export interface Proiect {
  id: string
  cod: string
  denumire: string
  beneficiar: string
  cuiBeneficiar: string | null
  adresaSantier: string
  stare: StareProiect
  dataInceput: string | null
  termenContractual: string | null
  creatLa: string
}

export interface CreeazaProiect {
  cod: string
  denumire: string
  beneficiar: string
  cuiBeneficiar: string | null
  adresaSantier: string
}

export interface Profil {
  id: string
  email: string
  nume: string
  tip: string
  firmaActiva: string | null
}

export type TipVersiuneDeviz = 'Oferta' | 'Contract' | 'BugetExecutie' | 'ActAditional'

export interface VersiuneDeviz {
  id: string
  tip: TipVersiuneDeviz
  numar: number
  denumire: string
  stare: 'Ciorna' | 'Aprobata' | 'Inlocuita'
  sursa: string
}

export interface ValoriResurse {
  material: number
  manopera: number
  utilaj: number
  transport: number
  total: number
}

export interface Recapitulatie {
  cheltuieliDirecteResurse: ValoriResurse
  contributiiManopera: number
  totalCheltuieliDirecte: number
  cheltuieliIndirecte: number
  profit: number
  totalFaraTva: number
  tva: number
  totalCuTva: number
}

export interface ParametriRecapitulatie {
  cotaContributiiManopera: number
  cotaCheltuieliIndirecte: number
  cotaProfit: number
  cotaTva: number
}

export interface ArticolDeviz {
  id: string
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  pretUnitar: ValoriResurse
  valoare: ValoriResurse
}

export interface CategorieDeviz {
  id: string
  cod: string
  denumire: string
  subcontractata: boolean
  total: number
  articole: ArticolDeviz[]
}

export interface ObiectDeviz {
  id: string
  cod: string
  denumire: string
  total: number
  categorii: CategorieDeviz[]
}

export interface StructuraDeviz {
  versiune: VersiuneDeviz
  parametri: ParametriRecapitulatie
  recapitulatie: Recapitulatie
  obiecte: ObiectDeviz[]
}

export interface ProblemaImport {
  severitate: 'Eroare' | 'Avertisment' | 'Informare'
  locatie: string
  mesaj: string
}

export interface PrevizualizareImport {
  format: string
  denumire: string
  obiecte: number
  articole: number
  recapitulatie: Recapitulatie | null
  probleme: ProblemaImport[]
  poateFiConfirmat: boolean
}
