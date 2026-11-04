import { CheckCircleOutlined, WarningOutlined } from '@ant-design/icons'
import type { CampCitit, Certitudine } from '../../api/aprovizionare'

/** Unitățile de măsură uzuale, propuse la completare. */
export const UNITATI = ['buc', 'kg', 't', 'm', 'ml', 'mp', 'mc', 'l', 'set', 'bax', 'cut', 'cuti', 'rola', 'sac', 'per', 'pac', 'kit', 'palet']

/**
 * Starea vizuală a unui câmp citit din document: „de verificat” cât timp valoarea propusă nu a fost schimbată.
 * Câmpurile lipsă sunt obligatorii (regula din formular), deci nu au nevoie de altă marcare.
 */
export function stareCamp<T>(camp: CampCitit<T> | undefined, valoareCurenta: unknown) {
  if (!camp || camp.certitudine !== 'Nesigur' || valoareCurenta !== camp.valoare) return {}
  return {
    validateStatus: 'warning' as const,
    help: camp.textSursa ? `De verificat. Citit în document: „${camp.textSursa}”` : 'De verificat',
  }
}

export const eticheteCertitudine: Record<Certitudine, { text: string; culoare: string; icon: React.ReactNode }> = {
  Sigur: { text: 'citit clar', culoare: 'green', icon: <CheckCircleOutlined /> },
  Nesigur: { text: 'de verificat', culoare: 'orange', icon: <WarningOutlined /> },
  Lipsa: { text: 'de completat', culoare: 'red', icon: <WarningOutlined /> },
}
