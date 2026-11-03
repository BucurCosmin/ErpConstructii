import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { DatePartener, Partener } from './tipuri'

const baza = '/api/parteneri/furnizori'

export const cheiParteneri = {
  toti: ['parteneri', 'furnizori'] as const,
}

export const useFurnizori = (inclusivInactivi: boolean) =>
  useQuery({
    queryKey: [...cheiParteneri.toti, inclusivInactivi],
    queryFn: () => api.get<Partener[]>(`${baza}?inclusivInactivi=${inclusivInactivi}`),
  })

function useModificareFurnizor<TArg>(fn: (arg: TArg) => Promise<unknown>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => qc.invalidateQueries({ queryKey: cheiParteneri.toti }),
  })
}

export const useCreeazaFurnizor = () =>
  useModificareFurnizor((date: DatePartener) => api.post<Partener>(baza, date))

export const useModificaFurnizor = () =>
  useModificareFurnizor(({ id, ...date }: { id: string } & DatePartener) =>
    api.put<void>(`${baza}/${id}`, date),
  )

export const useSchimbaActivareaFurnizorului = () =>
  useModificareFurnizor(({ id, activ }: { id: string; activ: boolean }) =>
    api.post<void>(`${baza}/${id}/${activ ? 'activare' : 'dezactivare'}`, {}),
  )
