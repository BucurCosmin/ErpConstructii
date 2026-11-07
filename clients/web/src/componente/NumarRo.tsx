import { InputNumber, type InputNumberProps } from 'antd'

/**
 * Număr în format românesc: VIRGULA desparte zecimalele, PUNCTUL grupează miile („1.234,56”).
 * La tastare, punctul e ignorat (separator de mii), virgula e separatorul zecimal; la ieșirea din câmp valoarea
 * se afișează grupată. Exemple: „1.234,56” → 1234,56; „44429,71” → 44.429,71; „7000” → 7.000.
 */
export function NumarRo(props: InputNumberProps<number>) {
  return (
    <InputNumber<number>
      {...props}
      formatter={(v, info) => {
        if (info.userTyping) return info.input
        if (v === undefined || v === null || `${v}` === '') return ''
        const n = Number(v)
        const text = props.precision !== undefined && !Number.isNaN(n) ? n.toFixed(props.precision) : `${v}`
        const [intreg, zecimale] = text.split('.')
        const grupat = intreg.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
        return zecimale !== undefined ? `${grupat},${zecimale}` : grupat
      }}
      parser={(text) => {
        const curat = (text ?? '').replace(/\s/g, '').replace(/\./g, '').replace(',', '.')
        const n = Number.parseFloat(curat)
        return Number.isNaN(n) ? (undefined as unknown as number) : n
      }}
    />
  )
}
