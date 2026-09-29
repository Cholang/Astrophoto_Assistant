import styles from './ProgressLine.module.css'

/**
 * 조용한 진행 표시 (DESIGN.md 9장: 스피너 대신 얇은 진행 바).
 * 끝을 알 수 없는 기다림(N.I.N.A. 준비 등)은 indeterminate로.
 */
export default function ProgressLine({ value = 0, label, indeterminate = false }: { value?: number; label: string; indeterminate?: boolean }) {
  const pct = Math.round(Math.min(1, Math.max(0, value)) * 100)
  return (
    <div
      className={styles.track}
      role="progressbar"
      aria-label={label}
      aria-valuemin={indeterminate ? undefined : 0}
      aria-valuemax={indeterminate ? undefined : 100}
      aria-valuenow={indeterminate ? undefined : pct}
    >
      <div className={indeterminate ? styles.moving : styles.fill} style={indeterminate ? undefined : { width: `${pct}%` }} />
    </div>
  )
}
