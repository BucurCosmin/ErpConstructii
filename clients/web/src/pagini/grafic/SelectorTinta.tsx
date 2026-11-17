import { TreeSelect } from 'antd'
import { useMemo } from 'react'
import { valoareTinta } from '../../api/grafic'
import type { ContinutGrafic } from '../../api/grafic'

interface NodArbore {
  value: string
  title: string
  cauta: string
  children?: NodArbore[]
}

/** Arborele țintelor (noduri WBS cu activitățile lor), construit o dată pe conținutul graficului. */
export function useArboreTinte(c: ContinutGrafic | undefined) {
  return useMemo(() => {
    if (!c) return []
    const copii = new Map<string | null, ContinutGrafic['noduri']>()
    for (const n of c.noduri) copii.set(n.cheieParinte, [...(copii.get(n.cheieParinte) ?? []), n])
    const activitati = new Map<string | null, ContinutGrafic['activitati']>()
    for (const a of c.activitati) activitati.set(a.cheieWbs, [...(activitati.get(a.cheieWbs) ?? []), a])
    const construieste = (parinte: string | null): NodArbore[] => [
      ...(copii.get(parinte) ?? []).map((n) => {
        const sub = construieste(n.cheie)
        return { value: valoareTinta('Wbs', n.cheie), title: `▸ ${n.cod} — ${n.denumire}`, cauta: `${n.cod} ${n.denumire}`.toLowerCase(), children: sub.length ? sub : undefined }
      }),
      ...(activitati.get(parinte) ?? []).map((a) => ({
        value: valoareTinta('Activitate', a.cod), title: `${a.cod} — ${a.denumire}`, cauta: `${a.cod} ${a.denumire}`.toLowerCase(),
      })),
    ]
    return construieste(null)
  }, [c])
}

/** Alegerea unei activități sau a unui nod WBS (▸) din actualizarea curentă a graficului, cu căutare după cod și denumire. */
export function SelectorTinta({ arbore, value, onChange, style, placeholder }: {
  arbore: NodArbore[]
  value?: string
  onChange?: (v: string | undefined) => void
  style?: React.CSSProperties
  placeholder?: string
}) {
  return (
    <TreeSelect<string> style={{ width: '100%', ...style }} value={value} onChange={onChange} treeData={arbore} allowClear
      placeholder={placeholder ?? 'Activitate sau nod WBS (▸)'} popupMatchSelectWidth={false} styles={{ popup: { root: { maxWidth: 720 } } }}
      showSearch={{ filterTreeNode: (t, n) => (n as unknown as NodArbore).cauta.includes(t.toLowerCase()) }}
      treeNodeLabelProp="title" listHeight={420} />
  )
}
