// Centrul de import: un fișier cu mai multe documente (facturi, note de comandă, necesare, bonuri de consum).

import { useMutation } from '@tanstack/react-query'
import { api } from './client'
import type { TipPagina } from './facturi'

export type TipDocumentImport = 'Factura' | 'NotaComanda' | 'Aviz' | 'NecesarMateriale' | 'BonConsum' | 'Anexe'

export interface PaginaImport {
  pagina: number
  tip: TipPagina
  descriere: string
  numar: string | null
}

export interface DocumentImport {
  tip: TipDocumentImport
  pagini: number[]
  numar: string | null
  data: string | null
}

export interface AnalizaImport {
  /** Identifică fișierul citit, păstrat 30 de minute pe server. */
  cheie: string
  numeFisier: string
  /** PDF cu mai multe pagini: documentele se taie în PDF-uri separate. */
  separabil: boolean
  dinOcr: boolean
  pagini: PaginaImport[]
  documente: DocumentImport[]
  probleme: { severitate: string; locatie: string; mesaj: string }[]
}

export const useAnalizeazaFisier = () =>
  useMutation({ mutationFn: (fisier: File) => api.postFisier<AnalizaImport>('/api/import-documente/analiza', fisier) })

/** Fișierul unui document: paginile lui, tăiate din fișierul citit (sau fișierul întreg, dacă nu se poate tăia). */
export async function fisierDocument(analiza: AnalizaImport, pagini: number[]): Promise<File> {
  const blob = await api.postBinar(`/api/import-documente/${analiza.cheie}/document`, { pagini })
  const toate = analiza.pagini.length
  if (!analiza.separabil || pagini.length === toate) return new File([blob], analiza.numeFisier, { type: blob.type })
  const baza = analiza.numeFisier.replace(/\.[^.]+$/, '')
  const interval = pagini.length === 1 ? `${pagini[0]}` : `${pagini[0]}-${pagini[pagini.length - 1]}`
  return new File([blob], `${baza}_p${interval}.pdf`, { type: 'application/pdf' })
}
