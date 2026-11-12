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
  /** D-47: „Sediu” = centru de cost al firmei (fără beneficiar), cu aceleași documente ca o lucrare. */
  tip: TipProiect
}

export type TipProiect = 'Lucrare' | 'Sediu'

export interface CreeazaProiect {
  tip: TipProiect
  cod: string
  denumire: string
  beneficiar: string | null
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
  /** Moneda prețului de ofertă (costurile directe sunt în lei) și cursul lei / unitate (null la RON). */
  moneda: string
  curs: number | null
  /** „Fara” la versiunile din ISDP: fără rotunjiri intermediare (D-50). */
  politicaRotunjire: 'Standard' | 'Fara'
  obiectiv: string | null
  obiect: string | null
  stadiuFizic: string | null
  /** Versiunea din care a pornit (ciorna de lucru, D-53); null la devizul importat. */
  versiuneAnterioaraId: string | null
}

export interface ValoriResurse {
  material: number
  manopera: number
  utilaj: number
  transport: number
  /** A cincea resursă (ISDP); 0 la devizele vechi. */
  echipament: number
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
  cheieStabila: string
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  /** Costul direct unitar și valoarea, în lei. */
  pretUnitar: ValoriResurse
  valoare: ValoriResurse
  tipArticol: 'Normat' | 'Asimilat' | 'ResursaDirecta'
  codWbs: string | null
  /** Prețul de ofertă (în moneda versiunii); null dacă articolul nu are. */
  pretOfertaUnitar: ValoriResurse | null
  valoareOferta: ValoriResurse | null
  resurse: number
}

/** Capitol al arborelui devizului (obiect, stadiu fizic, capitol), pe oricâte niveluri. */
export interface CapitolDeviz {
  id: string
  cod: string
  codWbs: string | null
  denumire: string
  tip: 'Obiect' | 'StadiuFizic' | 'Capitol'
  subcontractat: boolean
  /** Costul direct al capitolului (articolele lui și ale subcapitolelor). */
  total: number
  subcapitole: CapitolDeviz[]
  articole: ArticolDeviz[]
}

export interface StructuraDeviz {
  versiune: VersiuneDeviz
  parametri: ParametriRecapitulatie
  recapitulatie: Recapitulatie
  capitole: CapitolDeviz[]
}

export interface ProblemaImport {
  severitate: 'Eroare' | 'Avertisment' | 'Informare'
  locatie: string
  mesaj: string
}

export interface ComparatieTotal {
  rand: string
  declarat: number | null
  recalculat: number
}

export interface ArticolImport {
  codWbs: string | null
  capitol: string
  simbol: string
  denumire: string
  unitateMasura: string
  cantitate: number
  costDirect: number
  valoareOferta: number | null
  resurse: number
  tip: string
}

/** O resursă din extrasul devizului importat, cu starea în nomenclatorul firmei. */
export interface ResursaImport {
  cod: string
  denumire: string
  tip: string
  unitateMasura: string
  cantitateTotala: number
  valoare: number
  articole: number
  tipDedus: boolean
  nomenclator: 'Noua' | 'Nemapata' | 'Mapata'
  mapare: string | null
}

export interface DiferentaArticol {
  stare: 'Neschimbat' | 'Nou' | 'Eliminat' | 'CantitateSchimbata' | 'PretSchimbat' | 'CantitateSiPretSchimbate'
  simbol: string
  denumire: string
  unitateMasura: string
  codWbs: string | null
  cantitateVeche: number | null
  cantitateNoua: number | null
  pretVechi: number | null
  pretNou: number | null
  resurseSchimbate: boolean
  potrivitDupaDenumire: boolean
}

export interface ReimportDeviz {
  versiuneId: string
  versiune: string
  chei: number
  noi: number
  eliminate: number
  modificate: number
  diferente: DiferentaArticol[]
}

export interface PrevizualizareImport {
  format: string
  denumire: string
  capitole: number
  articole: number
  recapitulatie: Recapitulatie | null
  probleme: ProblemaImport[]
  poateFiConfirmat: boolean
  moneda: string
  curs: number | null
  politicaRotunjire: string
  totaluri: ComparatieTotal[] | null
  articoleLista: ArticolImport[] | null
  resurse: ResursaImport[] | null
  reimport: ReimportDeviz | null
  fisierDejaImportat: string | null
}

/** Fișierele importului de deviz: principalul (ISDP: deviz cu resurse) și, opțional, lista F3, cursul și moneda ofertei. */
export interface FisiereImportDeviz {
  fisier: File
  f3?: File | null
  curs?: number | null
  moneda?: string | null
  /** Denumirea versiunii (la confirmare); fără ea, titlul din fișier. */
  denumire?: string | null
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
