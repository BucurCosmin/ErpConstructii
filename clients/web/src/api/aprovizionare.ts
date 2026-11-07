import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface LinieNecesarMateriale {
  denumire: string
  unitateMasura: string
  cantitate: number
  observatii: string
  articolDevizId: string | null
}

export interface NecesarMateriale {
  id: string
  proiectId: string
  proiectCod: string
  lucrare: string
  compartimentSantier: string
  numar: string
  data: string
  stare: 'InAsteptareAprobare' | 'Aprobata' | 'Respinsa'
  emitentId: string
  emitentNume: string
  decisaDeId: string | null
  decisaDeNume: string | null
  decisaLa: string | null
  motivRespingere: string | null
  versiuneSablon: number
  linii: LinieNecesarMateriale[]
  sursa: 'Sistem' | 'Import'
  numarExtern: string | null
  areOriginal: boolean
}

export interface LinieNecesarMaterialeCerere {
  denumire: string
  unitateMasura: string
  cantitate: number
  observatii: string | null
  articolDevizId: string | null
}

export interface FiltreNecesareMateriale {
  proiectId?: string
  lucrare?: string
  material?: string
  dataDe?: string
  dataPanaLa?: string
  stare?: NecesarMateriale['stare']
  numar?: string
  intocmit?: string
  aprobatDe?: string
}

export const cheiAprovizionare = {
  necesareMateriale: ['aprovizionare', 'necesare-materiale'] as const,
}

export const useNecesareMateriale = (filtre: FiltreNecesareMateriale) =>
  useQuery({
    queryKey: [...cheiAprovizionare.necesareMateriale, filtre] as const,
    queryFn: () => {
      const parametri = new URLSearchParams()
      if (filtre.proiectId) parametri.set('proiectId', filtre.proiectId)
      if (filtre.lucrare) parametri.set('lucrare', filtre.lucrare)
      if (filtre.material) parametri.set('material', filtre.material)
      if (filtre.dataDe) parametri.set('dataDe', filtre.dataDe)
      if (filtre.dataPanaLa) parametri.set('dataPanaLa', filtre.dataPanaLa)
      if (filtre.stare) parametri.set('stare', filtre.stare)
      if (filtre.numar) parametri.set('numar', filtre.numar)
      if (filtre.intocmit) parametri.set('intocmit', filtre.intocmit)
      if (filtre.aprobatDe) parametri.set('aprobatDe', filtre.aprobatDe)
      const query = parametri.toString()
      return api.get<NecesarMateriale[]>(`/api/aprovizionare/necesare-materiale${query ? `?${query}` : ''}`)
    },
  })

export const useEmiteNecesarMateriale = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, compartimentSantier, linii }: { proiectId: string; compartimentSantier: string; linii: LinieNecesarMaterialeCerere[] }) =>
      api.post<NecesarMateriale>(`/api/aprovizionare/proiecte/${proiectId}/necesare-materiale`, { compartimentSantier, linii }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.necesareMateriale }),
  })
}

export const useAprobaNecesarMateriale = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id }: { proiectId: string; id: string }) =>
      api.post<NecesarMateriale>(`/api/aprovizionare/proiecte/${proiectId}/necesare-materiale/${id}/aprobare`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.necesareMateriale }),
  })
}

export const useRespingeNecesarMateriale = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id, motiv }: { proiectId: string; id: string; motiv: string }) =>
      api.post<NecesarMateriale>(`/api/aprovizionare/proiecte/${proiectId}/necesare-materiale/${id}/respingere`, { motiv }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.necesareMateriale }),
  })
}

// ---------------------------------------------------------------- Import necesare externe (scanări, Word)

export type Certitudine = 'Sigur' | 'Nesigur' | 'Lipsa'

export interface CampCitit<T> {
  valoare: T | null
  certitudine: Certitudine
  textSursa: string | null
}

export interface LinieInterpretata {
  rand: number
  denumire: CampCitit<string>
  unitateMasura: CampCitit<string>
  cantitate: CampCitit<number>
  observatii: CampCitit<string>
}

export interface InterpretareNecesar {
  numeFisier: string
  dinOcr: boolean
  numarExtern: CampCitit<string>
  data: CampCitit<string>
  compartimentSantier: CampCitit<string>
  lucrare: CampCitit<string>
  proiectPropus: { id: string; cod: string; denumire: string; scor: number; certitudine: Certitudine } | null
  linii: LinieInterpretata[]
  probleme: { severitate: 'Informare' | 'Avertisment' | 'Eroare'; locatie: string; mesaj: string }[]
  necesarExistent: string | null
}

export interface ConfirmaImportNecesar {
  numarExtern: string | null
  dataDocument: string
  compartimentSantier: string
  linii: LinieNecesarMaterialeCerere[]
}

/** Citește documentul (OCR pentru scanări) și propune datele; nu salvează nimic. */
export const useInterpreteazaNecesar = () =>
  useMutation({
    mutationFn: (fisier: File) => api.postFisier<InterpretareNecesar>('/api/aprovizionare/necesare-materiale/import/interpretare', fisier),
  })

/** Creează necesarul din documentul verificat; originalul se arhivează. */
export const useImportaNecesar = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ConfirmaImportNecesar }) =>
      api.postFisier<NecesarMateriale>(`/api/aprovizionare/proiecte/${proiectId}/necesare-materiale/import`, fisier, { date: JSON.stringify(date) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.necesareMateriale }),
  })
}

// ---------------------------------------------------------------- Note de comandă către furnizori (GC 007)

export interface LinieNotaComanda {
  denumire: string
  unitateMasura: string
  cantitate: number
  termen: string | null
}

export interface NotaComanda {
  id: string
  numar: string
  numarExtern: string | null
  data: string
  proiectId: string
  proiectCod: string
  lucrare: string | null
  furnizorId: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  linii: LinieNotaComanda[]
  observatii: string | null
  sursa: 'Sistem' | 'Import'
  areOriginal: boolean
  inregistrataDe: string
  inregistrataLa: string
  furnizorAdresa: string | null
  furnizorTelefon: string | null
  furnizorEmail: string | null
  conditii: ConditiiComanda
  necesarId: string | null
  areSablon: boolean
}

/** Condițiile bifate pe formularul GC 007; null = nebifat. */
export interface ConditiiComanda {
  receptie: 'LaFurnizor' | 'LaPrimire' | null
  clasaCalitate: 'Clasa4' | 'BestComercial' | null
  transport: 'Auto' | 'Cfr' | null
  plata: 'Op' | 'FilaCec' | 'Alte' | null
}

export interface FiltreNoteComanda {
  proiectId?: string
  furnizor?: string
  material?: string
  dataDe?: string
  dataPanaLa?: string
  cauta?: string
  lucrare?: string
  sursa?: 'Sistem' | 'Import'
}

export interface EmiteNotaComanda {
  data: string | null
  lucrare: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  furnizorAdresa: string | null
  furnizorTelefon: string | null
  furnizorEmail: string | null
  conditii: ConditiiComanda
  linii: LinieNotaComanda[]
  observatii: string | null
  necesarId: string | null
}

/** Emite o notă de comandă în aplicație (număr NC/AAAA/NNNN); furnizorul lipsă se adaugă în nomenclator. */
export const useEmiteNotaComanda = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, date }: { proiectId: string; date: EmiteNotaComanda }) =>
      api.post<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda`, date),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['aprovizionare', 'note-comanda'] }),
  })
}

export const useNoteComanda = (filtre: FiltreNoteComanda) =>
  useQuery({
    queryKey: ['aprovizionare', 'note-comanda', filtre] as const,
    queryFn: () => {
      const p = new URLSearchParams()
      for (const [k, v] of Object.entries(filtre)) if (v) p.set(k, v)
      const q = p.toString()
      return api.get<NotaComanda[]>(`/api/aprovizionare/note-comanda${q ? `?${q}` : ''}`)
    },
  })

export interface InterpretareNotaComanda {
  numeFisier: string
  dinOcr: boolean
  numarExtern: CampCitit<string>
  data: CampCitit<string>
  furnizorDenumire: CampCitit<string>
  furnizorCui: CampCitit<string>
  furnizorAdresa: string | null
  furnizorTelefon: string | null
  furnizorGasit: { id: string; denumire: string; cui: string | null } | null
  lucrare: CampCitit<string>
  proiectPropus: { id: string; cod: string; denumire: string; scor: number; certitudine: Certitudine } | null
  linii: { rand: number; denumire: CampCitit<string>; unitateMasura: CampCitit<string>; cantitate: CampCitit<number>; termen: string | null }[]
  probleme: { severitate: 'Informare' | 'Avertisment' | 'Eroare'; locatie: string; mesaj: string }[]
  comandaExistenta: string | null
}

export interface ConfirmaNotaComanda {
  numarExtern: string | null
  data: string
  lucrare: string | null
  furnizorDenumire: string
  furnizorCui: string | null
  furnizorAdresa: string | null
  furnizorTelefon: string | null
  linii: LinieNotaComanda[]
  observatii: string | null
}

/** Citește nota de comandă (OCR pentru scanări) și propune datele; nu salvează nimic. */
export const useInterpreteazaNotaComanda = () =>
  useMutation({
    mutationFn: (fisier: File) => api.postFisier<InterpretareNotaComanda>('/api/aprovizionare/note-comanda/import/interpretare', fisier),
  })

/** Înregistrează nota de comandă verificată; originalul se arhivează, furnizorul lipsă se adaugă în nomenclator. */
export const useImportaNotaComanda = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ConfirmaNotaComanda }) =>
      api.postFisier<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda/import`, fisier, { date: JSON.stringify(date) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['aprovizionare', 'note-comanda'] }),
  })
}
