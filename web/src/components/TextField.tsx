import { useState, type InputHTMLAttributes } from 'react'
import { clampBytes } from '../profiles'
import styles from './TextField.module.css'

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'maxLength'> & {
  id: string
  label: string
  value: string
  onValueChange: (value: string) => void
  /** 최대 byte (한글 2, 영문 1). 넘치는 부분은 입력되지 않는다. */
  maxBytes?: number
}

/**
 * 이름표 + 입력창. 제한은 입력창 안의 힌트 문구로 알리고, 글자 수 표시는 두지 않는다.
 * 한글 조합 중에는 자르지 않고, 조합이 끝난 뒤에 최대 byte로 맞춘다.
 */
export default function TextField({ id, label, value, onValueChange, maxBytes, ...rest }: Props) {
  const [composing, setComposing] = useState(false)
  const clamp = (v: string) => (maxBytes ? clampBytes(v, maxBytes) : v)

  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>
        {label}
      </label>
      <input
        id={id}
        className={styles.input}
        value={value}
        onChange={(e) => onValueChange(composing ? e.target.value : clamp(e.target.value))}
        onCompositionStart={() => setComposing(true)}
        onCompositionEnd={(e) => {
          setComposing(false)
          onValueChange(clamp(e.currentTarget.value))
        }}
        {...rest}
      />
    </div>
  )
}
