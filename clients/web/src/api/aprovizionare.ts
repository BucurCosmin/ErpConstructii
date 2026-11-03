import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface LinieNotaComanda {
  denumire: string
  unitateMasura: string
  cantitate: number
  observatii: string
  articolDevizId: string | null
}

export interface NotaComanda {
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
  linii: LinieNotaComanda[]
  sursa: 'Sistem' | 'Import'
  numarExtern: string | null
  areOriginal: boolean
}

export interface LinieNotaComandaCerere {
  denumire: string
  unitateMasura: string
  cantitate: number
  observatii: string | null
  articolDevizId: string | null
}

export interface FiltreNoteComanda {
  proiectId?: string
  lucrare?: string
  material?: string
  dataDe?: string
  dataPanaLa?: string
  stare?: NotaComanda['stare']
  numar?: string
  intocmit?: string
  aprobatDe?: string
}

export const cheiAprovizionare = {
  noteComanda: ['aprovizionare', 'note-comanda'] as const,
}

export const useNoteComanda = (filtre: FiltreNoteComanda) =>
  useQuery({
    queryKey: [...cheiAprovizionare.noteComanda, filtre] as const,
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
      return api.get<NotaComanda[]>(`/api/aprovizionare/note-comanda${query ? `?${query}` : ''}`)
    },
  })

export const useEmiteNotaComanda = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, compartimentSantier, linii }: { proiectId: string; compartimentSantier: string; linii: LinieNotaComandaCerere[] }) =>
      api.post<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda`, { compartimentSantier, linii }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.noteComanda }),
  })
}

export const useAprobaNotaComanda = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id }: { proiectId: string; id: string }) =>
      api.post<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda/${id}/aprobare`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.noteComanda }),
  })
}

export const useRespingeNotaComanda = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, id, motiv }: { proiectId: string; id: string; motiv: string }) =>
      api.post<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda/${id}/respingere`, { motiv }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.noteComanda }),
  })
}

// ---------------------------------------------------------------- Import note externe (scanări, Word)

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

export interface InterpretareNota {
  numeFisier: string
  dinOcr: boolean
  numarExtern: CampCitit<string>
  data: CampCitit<string>
  compartimentSantier: CampCitit<string>
  lucrare: CampCitit<string>
  proiectPropus: { id: string; cod: string; denumire: string; scor: number; certitudine: Certitudine } | null
  linii: LinieInterpretata[]
  probleme: { severitate: 'Informare' | 'Avertisment' | 'Eroare'; locatie: string; mesaj: string }[]
  notaExistenta: string | null
}

export interface ConfirmaImportNota {
  numarExtern: string | null
  dataDocument: string
  compartimentSantier: string
  linii: LinieNotaComandaCerere[]
}

/** Citește documentul (OCR pentru scanări) și propune datele; nu salvează nimic. */
export const useInterpreteazaNota = () =>
  useMutation({
    mutationFn: (fisier: File) => api.postFisier<InterpretareNota>('/api/aprovizionare/note-comanda/import/interpretare', fisier),
  })

/** Creează nota din documentul verificat; originalul se arhivează. */
export const useImportaNota = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ proiectId, fisier, date }: { proiectId: string; fisier: File; date: ConfirmaImportNota }) =>
      api.postFisier<NotaComanda>(`/api/aprovizionare/proiecte/${proiectId}/note-comanda/import`, fisier, { date: JSON.stringify(date) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAprovizionare.noteComanda }),
  })
}
