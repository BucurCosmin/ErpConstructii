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
  /** D-39: arhivat = invizibil în liste și alegeri, fără documente noi; se reactivează oricând. */
  arhivat: boolean
  arhivatLa: string | null
  arhivatDe: string | null
}

export interface CreeazaProiect {
  cod: string
  denumire: string
  beneficiar: string
  cuiBeneficiar: string | null
  adresaSantier: string
}

export interface Firma {
  id: string
  denumire: string
  cui: string | null
}

export interface Adresa {
  strada: string
  localitate: string
  judet: string | null
  codPostal: string | null
  tara: string
}

export interface ContBancar {
  banca: string
  iban: string
  moneda: string
  principal: boolean
}

export interface DateFirma {
  id: string
  denumire: string
  cui: string | null
  nrRegCom: string | null
  adresa: Adresa
  telefon: string | null
  fax: string | null
  email: string | null
  website: string | null
  conturiBancare: ContBancar[]
}

export interface Profil {
  id: string
  email: string
  nume: string
  tip: string
  firmaActiva: string | null
  firme: Firma[]
  mfaActivat: boolean
}

export interface Partener {
  id: string
  denumire: string
  cui: string | null
  codFiscal: string | null
  nrRegCom: string | null
  platitorTva: boolean
  adresa: Adresa
  telefon: string | null
  email: string | null
  persoanaContact: string | null
  telefonContact: string | null
  observatii: string | null
  conturiBancare: ContBancar[]
  esteClient: boolean
  esteSubantreprenor: boolean
  activ: boolean
}

export type DatePartener = Omit<Partener, 'id' | 'codFiscal' | 'activ'>

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

// ---------------------------------------------------------------- Administrare

export type TipUtilizator = 'AdministratorSistem' | 'AdministratorFirma' | 'Intern' | 'Santier' | 'Auditor' | 'ContServiciu'

export interface PermisiuneCatalog {
  cod: string
  denumire: string
}

export interface ModulCatalog {
  cod: string
  denumire: string
  permisiuni: PermisiuneCatalog[]
}

export interface CatalogPermisiuni {
  module: ModulCatalog[]
  dateSensibile: PermisiuneCatalog[]
}

export interface UtilizatorLista {
  id: string
  nume: string
  email: string
  tip: TipUtilizator
  activ: boolean
  expiraLa: string | null
  areAcces: boolean
  mfaActivat: boolean
  blocat: boolean
  trebuieSchimbataParola: boolean
  roluri: string[]
}

export interface AtribuireUtilizator {
  id: string
  rolId: string
  rol: string
  proiectId: string | null
  valabilDe: string
  valabilPana: string | null
  stare: 'activa' | 'viitoare' | 'incheiata'
}

export interface UtilizatorDetalii {
  utilizator: UtilizatorLista
  atribuiri: AtribuireUtilizator[]
  poateModificaContul: boolean
  alteFirme: number
}

export interface CreeazaUtilizator {
  email: string
  nume: string
  tip: TipUtilizator
  rolId: string
  proiectId: string | null
  valabilDe: string | null
}

export interface UtilizatorCreat {
  id: string
  parolaTemporara: string | null
  existent: boolean
}

export interface RolFirma {
  id: string
  cod: string
  denumire: string
  permisiuni: string[]
  predefinit: boolean
  utilizatori: number
}

export interface FirmaAdmin {
  id: string
  denumire: string
  cui: string | null
  activa: boolean
  utilizatori: number
}
