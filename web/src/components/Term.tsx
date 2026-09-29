import InfoTip from './InfoTip'
import styles from './Term.module.css'

/** 전문용어 표기: 한글(원어) + 물음표 툴팁 한 줄 설명 (DESIGN.md 6장). */
export default function Term({ label, term, hint }: { label: string; term?: string | null; hint?: string | null }) {
  return (
    <span className={styles.term}>
      <span className={styles.label}>{label}</span>
      {term && <span className={styles.original}>({term})</span>}
      {hint && <InfoTip label={`${label} 설명`} text={hint} />}
    </span>
  )
}
