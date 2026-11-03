import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

// Clientul OIDC al aplicației web: Authorization Code + PKCE către serverul OpenIddict din API.
// Token-urile stau în sessionStorage (per tab); un tab nou obține token-uri fără parolă,
// cât timp cookie-ul de sesiune al serverului e valabil.

const origine = window.location.origin

export const managerAutentificare = new UserManager({
  authority: `${origine}/`,
  client_id: 'erp-web',
  redirect_uri: `${origine}/autentificare/callback`,
  post_logout_redirect_uri: `${origine}/`,
  response_type: 'code',
  scope: 'openid profile email offline_access',
  loadUserInfo: false,
  // Reînnoire automată cu refresh token-ul (rotit de server la fiecare folosire).
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
})

/** Access token valabil; încearcă reînnoirea dacă a expirat. Null = trebuie login. */
export async function tokenCurent(): Promise<string | null> {
  const user = await managerAutentificare.getUser()
  if (user && !user.expired) return user.access_token
  return reinnoieste()
}

let reinnoireInCurs: Promise<string | null> | null = null

/**
 * Reînnoire cu refresh token-ul. Null dacă sesiunea nu mai poate fi reînnoită.
 * Cererile simultane (ex. mai multe 401 deodată) împart aceeași reînnoire.
 */
export function reinnoieste(extra?: Record<string, string>): Promise<string | null> {
  if (reinnoireInCurs && !extra) return reinnoireInCurs
  const promisiune = managerAutentificare
    .signinSilent(extra ? { extraTokenParams: extra } : undefined)
    .then((user) => user?.access_token ?? null)
    .catch(() => null)
    .finally(() => {
      if (reinnoireInCurs === promisiune) reinnoireInCurs = null
    })
  reinnoireInCurs = promisiune
  return promisiune
}

/** Trimite la login, cu revenire la pagina curentă. */
export function cereAutentificare(): Promise<void> {
  return managerAutentificare.signinRedirect({ url_state: window.location.pathname + window.location.search })
}

/** Schimbă firma activă: serverul emite token-uri noi cu alt firma_id (dacă utilizatorul are acces). */
export async function schimbaFirma(firmaId: string): Promise<void> {
  const token = await reinnoieste({ firma_id: firmaId })
  if (!token) throw new Error('Firma nu a putut fi schimbată. Autentifică-te din nou.')
}

export function iesire(): Promise<void> {
  return managerAutentificare.signoutRedirect()
}

/** URL de revenire acceptat după login: doar cererea de autorizare a serverului (fără open redirect). */
export function urlRevenireValid(url: string | null): url is string {
  return !!url && url.startsWith('/connect/authorize?')
}
