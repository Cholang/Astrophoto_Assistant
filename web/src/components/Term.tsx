import { useId, useState } from 'react'
import styles from './Term.module.css'

/**
 * 전문용어 표기: 한글(원어) + 물음표 툴팁 한 줄 설명 (DESIGN.md 6장).
 * 마우스를 올리거나, 키보드로 초점을 옮기거나, 눌러서 연다.
 */
export default function Term({ label, term, hint }: { label: string; term?: string | null; hint?: string | null }) {
  const [open, setOpen] = useState(false)
  const id = useId()

  return (
    <span className={styles.term}>
      <span className={styles.label}>{label}</span>
      {term && <span className={styles.original}>({term})</span>}
      {hint && (
        <span className={styles.anchor} onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
          <button
            type="button"
            className={styles.q}
            aria-label={`${label} 설명`}
            aria-describedby={open ? id : undefined}
            aria-expanded={open}
            onClick={() => setOpen((o) => !o)}
            onFocus={() => setOpen(true)}
            onBlur={() => setOpen(false)}
          >
            ?
          </button>
          {open && (
            <span role="tooltip" id={id} className={styles.tip}>
              {hint}
            </span>
          )}
        </span>
      )}
    </span>
  )
}
