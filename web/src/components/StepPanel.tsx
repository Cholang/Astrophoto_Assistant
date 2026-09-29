import type { CheckItem } from '../checks'
import DiagnosisBody from './DiagnosisCard'
import ProgressLine from './ProgressLine'
import StatusIcon from './StatusIcon'
import styles from './StepPanel.module.css'

/**
 * 단계 화면 하단의 공통 영역: 현재 항목의 이름(원어), 한 줄 설명, 결과.
 * 실패하면 같은 자리에 진단(무슨 일 / 왜 / 이렇게 / 원본)을 보여 준다.
 */
export default function StepPanel({ item, index, total }: { item: CheckItem; index: number; total: number }) {
  return (
    <section className={styles.panel} data-status={item.status} aria-live="polite">
      <header className={styles.head}>
        <p className={styles.position}>
          {index + 1} / {total}
        </p>
        <h2>
          {item.title}
          {item.term && <span className={styles.term}> ({item.term})</span>}
        </h2>
        {item.hint && <p className={styles.hint}>{item.hint}</p>}
      </header>

      {item.status === 'Running' && <ProgressLine indeterminate label={`${item.title} 진행 중`} />}

      {item.message && (
        <p className={styles.result}>
          <StatusIcon status={item.status} />
          {item.message}
        </p>
      )}

      {item.diagnosis && (item.status === 'Fail' || item.status === 'Warn') && (
        <DiagnosisBody diagnosis={item.diagnosis} tone={item.status === 'Fail' ? 'fail' : 'warn'} />
      )}
    </section>
  )
}
