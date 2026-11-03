// Client HTTP comun: atașează access token-ul și, la 401, încearcă o reînnoire înainte de a cere login.

import { cereAutentificare, reinnoieste, tokenCurent } from './autentificare'

export class EroareApi extends Error {
  readonly status: number
  readonly detalii: unknown

  constructor(status: number, mesaj: string, detalii?: unknown) {
    super(mesaj)
    this.status = status
    this.detalii = detalii
  }
}

async function trimite(url: string, init: RequestInit = {}): Promise<Response> {
  const cuToken = async (token: string | null) => {
    const h = new Headers(init.headers)
    if (token) h.set('Authorization', `Bearer ${token}`)
    return fetch(url, { ...init, headers: h })
  }

  let r = await cuToken(await tokenCurent())
  if (r.status === 401) {
    const nou = await reinnoieste()
    if (!nou) {
      await cereAutentificare()
      throw new EroareApi(401, 'Sesiunea a expirat.')
    }
    r = await cuToken(nou)
  }
  return r
}

async function trateaza<T>(r: Response): Promise<T> {
  const text = await r.text()
  const corp: unknown = text ? JSON.parse(text) : null
  if (!r.ok) {
    const mesaj =
      (corp as { mesaj?: string } | null)?.mesaj ?? (corp as { title?: string } | null)?.title ?? mesajImplicit(r.status)
    throw new EroareApi(r.status, mesaj, corp)
  }
  return corp as T
}

function mesajImplicit(status: number): string {
  switch (status) {
    case 401:
      return 'Nu ești autentificat.'
    case 403:
      return 'Nu ai drept pentru această operație.'
    case 404:
      return 'Înregistrarea nu a fost găsită.'
    default:
      return `Eroare de server (${status}).`
  }
}

export const api = {
  get: async <T>(url: string) => trateaza<T>(await trimite(url)),

  post: async <T>(url: string, corp: unknown) =>
    trateaza<T>(await trimite(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(corp) })),

  put: async <T>(url: string, corp: unknown) =>
    trateaza<T>(await trimite(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(corp) })),

  delete: async <T>(url: string) => trateaza<T>(await trimite(url, { method: 'DELETE' })),

  postFisier: async <T>(url: string, fisier: File) => {
    const date = new FormData()
    date.append('fisier', fisier)
    return trateaza<T>(await trimite(url, { method: 'POST', body: date }))
  },
}

/** Cereri către endpoint-urile de login (/api/cont/*): cu cookie-ul de sesiune, fără token. */
export const apiCont = {
  get: async <T>(url: string) => trateaza<T>(await fetch(url, { credentials: 'same-origin' })),
  post: async <T>(url: string, corp: unknown) =>
    trateaza<T>(
      await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corp),
      }),
    ),
}
