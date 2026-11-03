import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  CatalogPermisiuni,
  CreeazaUtilizator,
  Firma,
  FirmaAdmin,
  RolFirma,
  TipUtilizator,
  UtilizatorCreat,
  UtilizatorDetalii,
  UtilizatorLista,
} from './tipuri'

const baza = '/api/administrare'

export const cheiAdmin = {
  toate: ['administrare'] as const,
  catalog: ['administrare', 'catalog'] as const,
  utilizatori: ['administrare', 'utilizatori'] as const,
  utilizator: (id: string) => ['administrare', 'utilizatori', id] as const,
  roluri: ['administrare', 'roluri'] as const,
  firma: ['administrare', 'firma'] as const,
  firme: ['administrare', 'firme'] as const,
}

export const useCatalogPermisiuni = () =>
  useQuery({ queryKey: cheiAdmin.catalog, queryFn: () => api.get<CatalogPermisiuni>(`${baza}/permisiuni`), staleTime: Infinity })

export const useUtilizatori = () =>
  useQuery({ queryKey: cheiAdmin.utilizatori, queryFn: () => api.get<UtilizatorLista[]>(`${baza}/utilizatori`) })

export const useUtilizator = (id: string | null) =>
  useQuery({
    queryKey: cheiAdmin.utilizator(id ?? ''),
    queryFn: () => api.get<UtilizatorDetalii>(`${baza}/utilizatori/${id}`),
    enabled: !!id,
  })

export const useRoluri = () => useQuery({ queryKey: cheiAdmin.roluri, queryFn: () => api.get<RolFirma[]>(`${baza}/roluri`) })

export const useFirmaActiva = () => useQuery({ queryKey: cheiAdmin.firma, queryFn: () => api.get<Firma>(`${baza}/firma`) })

export const useFirme = (activ: boolean) =>
  useQuery({ queryKey: cheiAdmin.firme, queryFn: () => api.get<FirmaAdmin[]>(`${baza}/firme`), enabled: activ })

/** Orice modificare de administrare reîncarcă listele (utilizatori, roluri, contoare). */
function useModificare<TArg, TRez = void>(fn: (arg: TArg) => Promise<TRez>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiAdmin.toate }),
  })
}

export const useCreeazaUtilizator = () =>
  useModificare((c: CreeazaUtilizator) => api.post<UtilizatorCreat>(`${baza}/utilizatori`, c))

export const useModificaUtilizator = () =>
  useModificare(({ id, ...c }: { id: string; nume: string; tip: TipUtilizator; expiraLa: string | null }) =>
    api.put<void>(`${baza}/utilizatori/${id}`, c),
  )

export const useActiune = () =>
  useModificare(({ id, actiune }: { id: string; actiune: 'dezactivare' | 'activare' | 'resetare-mfa' }) =>
    api.post<void>(`${baza}/utilizatori/${id}/${actiune}`, {}),
  )

export const useReseteazaParola = () =>
  useModificare((id: string) => api.post<{ parolaTemporara: string }>(`${baza}/utilizatori/${id}/resetare-parola`, {}))

export const useAdaugaAtribuire = () =>
  useModificare(
    ({ id, ...c }: { id: string; rolId: string; proiectId: string | null; valabilDe: string; valabilPana: string | null }) =>
      api.post<void>(`${baza}/utilizatori/${id}/atribuiri`, c),
  )

export const useRevocaAtribuire = () => useModificare((id: string) => api.post<void>(`${baza}/atribuiri/${id}/revocare`, {}))

export const useCreeazaRol = () =>
  useModificare((c: { cod: string; denumire: string; permisiuni: string[] }) => api.post<{ id: string }>(`${baza}/roluri`, c))

export const useModificaRol = () =>
  useModificare(({ id, ...c }: { id: string; denumire: string; permisiuni: string[] }) => api.put<void>(`${baza}/roluri/${id}`, c))

export const useRestaureazaRol = () => useModificare((id: string) => api.post<void>(`${baza}/roluri/${id}/restaurare`, {}))

export const useStergeRol = () => useModificare((id: string) => api.delete<void>(`${baza}/roluri/${id}`))

export const useModificaFirma = () =>
  useModificare((c: { denumire: string; cui: string | null }) => api.put<void>(`${baza}/firma`, c))

export const useCreeazaFirma = () =>
  useModificare((c: { denumire: string; cui: string | null }) => api.post<Firma>(`${baza}/firme`, c))
