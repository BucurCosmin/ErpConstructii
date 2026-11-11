import dayjs from 'dayjs'
import type { StareProiect, TipUtilizator, TipVersiuneDeviz, VersiuneDeviz } from '../api/tipuri'

const lei = new Intl.NumberFormat('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const leiCompact = new Intl.NumberFormat('ro-RO', { notation: 'compact', maximumFractionDigits: 1 })
const cantitate = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 4 })
const procent = new Intl.NumberFormat('ro-RO', { style: 'percent', maximumFractionDigits: 2 })

export const formatLei = (v: number) => lei.format(v)
export const formatLeiCompact = (v: number) => `${leiCompact.format(v)} lei`
export const formatCantitate = (v: number) => cantitate.format(v)
export const formatProcent = (v: number) => procent.format(v)
/** Proiectul în liste și alegeri: „COD — Denumire”, cu „(sediu)” pentru sediile firmei (D-47). */
export const etichetaProiect = (p: { cod: string; denumire: string; tip?: string }) =>
  `${p.cod} — ${p.denumire}${p.tip === 'Sediu' ? ' (sediu)' : ''}`
export const formatData = (v: string | null | undefined) => (v ? dayjs(v).format('DD.MM.YYYY') : '—')

export const stariProiect: Record<StareProiect, { eticheta: string; culoare: string }> = {
  Ofertare: { eticheta: 'Ofertare', culoare: 'blue' },
  Contractat: { eticheta: 'Contractat', culoare: 'cyan' },
  InExecutie: { eticheta: 'În execuție', culoare: 'green' },
  ReceptieLaTerminare: { eticheta: 'Recepție la terminare', culoare: 'gold' },
  PerioadaGarantie: { eticheta: 'Perioadă de garanție', culoare: 'purple' },
  Inchis: { eticheta: 'Închis', culoare: 'default' },
  Pierdut: { eticheta: 'Pierdut', culoare: 'red' },
}

export const tipuriVersiune: Record<TipVersiuneDeviz, string> = {
  Oferta: 'Ofertă',
  Contract: 'Contract',
  BugetExecutie: 'Buget de execuție',
  ActAditional: 'Act adițional',
}

export const stariVersiune: Record<VersiuneDeviz['stare'], { eticheta: string; culoare: string }> = {
  Ciorna: { eticheta: 'Ciornă', culoare: 'default' },
  Aprobata: { eticheta: 'Aprobată', culoare: 'green' },
  Inlocuita: { eticheta: 'Înlocuită', culoare: 'orange' },
}

/** Procentul de timp scurs din durata contractuală (0–100), sau null dacă lipsesc datele. */
export function timpScurs(inceput: string | null, termen: string | null, azi = dayjs()): number | null {
  if (!inceput || !termen) return null
  const total = dayjs(termen).diff(dayjs(inceput), 'day')
  if (total <= 0) return null
  const scurs = azi.diff(dayjs(inceput), 'day')
  return Math.max(0, Math.min(100, Math.round((scurs / total) * 100)))
}

export const tipuriUtilizator: Record<TipUtilizator, { eticheta: string; descriere: string }> = {
  AdministratorSistem: { eticheta: 'Administrator de sistem', descriere: 'Administrează toate firmele și conturile.' },
  AdministratorFirma: { eticheta: 'Administrator firmă', descriere: 'Administrează utilizatorii și rolurile firmei.' },
  Intern: { eticheta: 'Intern (birou)', descriere: 'Lucrează în aplicația web.' },
  Santier: { eticheta: 'Șantier (mobil)', descriere: 'Lucrează în principal din aplicația mobilă.' },
  Auditor: { eticheta: 'Auditor', descriere: 'Cont doar pentru citire, cu dată de expirare.' },
  ContServiciu: { eticheta: 'Cont de serviciu', descriere: 'Pentru integrări automate.' },
}
