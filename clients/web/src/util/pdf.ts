import { api } from '../api/client'

/**
 * Deschide un PDF generat de API într-un tab nou (cererea poartă token-ul, deci nu poate fi un simplu link).
 * Tab-ul se deschide înainte de cerere, ca browserul să nu-l blocheze ca fereastră pop-up.
 */
export async function deschidePdf(url: string) {
  const tab = window.open('', '_blank')
  try {
    const blob = await api.getBinar(url)
    const adresa = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }))
    if (tab) tab.location.href = adresa
    else window.location.href = adresa
    setTimeout(() => URL.revokeObjectURL(adresa), 120_000)
  } catch (e) {
    tab?.close()
    throw e
  }
}
