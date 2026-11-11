import { Select } from 'antd'
import { useCompartimente, useSantiere } from '../api/proiecte'
import type { Santier } from '../api/proiecte'

/**
 * Valoarea aleasă: „S:<id>” = șantier al proiectului. „C:<id>” (compartiment al firmei) apare doar la necesarele emise
 * înainte pe un compartiment, care îl păstrează la modificare; necesarele noi se emit pe un șantier al proiectului.
 */
export type ValoareSolicitant = string

export const solicitantDin = (v?: ValoareSolicitant | null) => ({
  santierId: v?.startsWith('S:') ? v.slice(2) : null,
  compartimentId: v?.startsWith('C:') ? v.slice(2) : null,
})

export const valoareSolicitant = (santierId?: string | null, compartimentId?: string | null): ValoareSolicitant | undefined =>
  santierId ? `S:${santierId}` : compartimentId ? `C:${compartimentId}` : undefined

const cheie = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().replace(/[^A-Z0-9]+/g, ' ').trim()

/** Textul de pe un document (OCR / necesar vechi) potrivit cu un șantier al proiectului: denumire identică sau, unică, pe cuvinte întregi. */
export function potrivesteSolicitant(text: string | null | undefined, santiere: Santier[]): ValoareSolicitant | undefined {
  const t = cheie(text ?? '')
  if (!t) return undefined
  const locuri = santiere.map((s) => ({ v: `S:${s.id}`, d: cheie(s.denumire) }))
  const identice = locuri.filter((l) => l.d === t)
  if (identice.length) return identice.length === 1 ? identice[0].v : undefined
  const partiale = locuri.filter((l) => l.d.length >= 3 && (` ${t} `.includes(` ${l.d} `) || ` ${l.d} `.includes(` ${t} `)))
  return partiale.length === 1 ? partiale[0].v : undefined
}

/** Cine cere materialele: un șantier al proiectului ales. */
export function SelectSolicitant({ proiectId, value, onChange, disabled }: {
  proiectId?: string
  value?: ValoareSolicitant
  onChange?: (v: ValoareSolicitant) => void
  disabled?: boolean
}) {
  const santiere = useSantiere(proiectId)
  // Doar pentru afișarea compartimentului unui necesar vechi (nu se mai poate alege altul).
  const vechi = value?.startsWith('C:') ? value.slice(2) : undefined
  const compartimente = useCompartimente(true)
  const compartiment = vechi ? compartimente.data?.find((c) => c.id === vechi) : undefined
  return (
    <Select
      value={value}
      onChange={onChange}
      disabled={disabled || !proiectId}
      showSearch={{ optionFilterProp: 'label' }}
      loading={santiere.isLoading}
      placeholder={proiectId ? 'Alege șantierul' : 'Alege întâi proiectul'}
      notFoundContent="Proiectul nu are șantiere: adaugă-le în fișa proiectului (tab-ul „Șantiere”)."
      options={[
        ...(santiere.data ?? []).map((s) => ({ value: `S:${s.id}`, label: s.adresa ? `${s.denumire} · ${s.adresa}` : s.denumire })),
        ...(vechi ? [{ value: value!, label: `${compartiment?.denumire ?? 'Compartiment'} (compartiment, necesar emis înainte)` }] : []),
      ]}
    />
  )
}
