// Client HTTP comun. În Development, identitatea vine din headerele X-Dev-* (vezi AutentificareDev.cs);
// în producție va fi un access token OpenIddict (D-07), aceeași interfață pentru apelanți.

export interface SesiuneDev {
  utilizatorId: string
  firmaId: string
  firmaNume: string
}

let sesiune: SesiuneDev | null = null

export class EroareApi extends Error {
  readonly status: number
  readonly detalii: unknown

  constructor(status: number, mesaj: string, detalii?: unknown) {
    super(mesaj)
    this.status = status
    this.detalii = detalii
  }
}

/** Încarcă identitatea demo. Apelat o dată, la pornirea aplicației. */
export async function initializeazaSesiunea(): Promise<SesiuneDev> {
  const r = await fetch('/dev/sesiune')
  if (!r.ok) {
    throw new EroareApi(
      r.status,
      r.status === 404
        ? 'Utilizatorul demo nu există. Rulează migrările și repornește API-ul.'
        : 'API-ul nu răspunde. Pornește-l cu „dotnet run --project src/Host/ErpConstructii.Api”.',
    )
  }
  sesiune = (await r.json()) as SesiuneDev
  return sesiune
}

export function sesiuneCurenta(): SesiuneDev | null {
  return sesiune
}

function headere(extra?: HeadersInit): Headers {
  const h = new Headers(extra)
  if (sesiune) {
    h.set('X-Dev-Utilizator', sesiune.utilizatorId)
    h.set('X-Dev-Firma', sesiune.firmaId)
  }
  return h
}

async function trateaza<T>(r: Response): Promise<T> {
  const text = await r.text()
  const corp: unknown = text ? JSON.parse(text) : null
  if (!r.ok) {
    const mesaj =
      (corp as { mesaj?: string; title?: string } | null)?.mesaj ??
      (corp as { title?: string } | null)?.title ??
      mesajImplicit(r.status)
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
  get: async <T>(url: string) => trateaza<T>(await fetch(url, { headers: headere() })),

  post: async <T>(url: string, corp: unknown) =>
    trateaza<T>(
      await fetch(url, {
        method: 'POST',
        headers: headere({ 'Content-Type': 'application/json' }),
        body: JSON.stringify(corp),
      }),
    ),

  postFisier: async <T>(url: string, fisier: File) => {
    const date = new FormData()
    date.append('fisier', fisier)
    return trateaza<T>(await fetch(url, { method: 'POST', headers: headere(), body: date }))
  },
}
