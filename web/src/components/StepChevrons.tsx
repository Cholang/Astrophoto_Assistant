import StatusIcon, { type Status } from './StatusIcon'
import styles from './StepChevrons.module.css'

export interface ChevronStep {
  id: string
  title: string
  term?: string | null
  status: Status
}

const STATUS_WORD: Record<Status, string> = {
  Pending: '대기',
  Running: '확인 중',
  Pass: '통과',
  Fail: '필요',
  Warn: '권장',
  Skipped: '보류',
}

/**
 * 한 단계 안의 하위 항목을 가로 화살표 띠로 보여 준다 (설치 확인, 엔진 켜기 등).
 * 현재 칸은 채워서 강조하고, 칸을 누르면 그 항목이 현재가 되어 아래 공통 영역 설명이 바뀐다.
 */
export default function StepChevrons({
  steps,
  current,
  onSelect,
  label,
}: {
  steps: ChevronStep[]
  current: string
  onSelect: (id: string) => void
  label: string
}) {
  return (
    <ol className={styles.strip} aria-label={label}>
      {steps.map((s, i) => {
        const isCurrent = s.id === current
        return (
          <li key={s.id} className={styles.item}>
            <button
              type="button"
              className={styles.chevron}
              data-status={s.status}
              data-current={isCurrent}
              aria-current={isCurrent ? 'step' : undefined}
              aria-label={`${i + 1}. ${s.title}${s.term ? ` (${s.term})` : ''}: ${STATUS_WORD[s.status]}`}
              onClick={() => onSelect(s.id)}
            >
              <StatusIcon status={s.status} />
              <span className={styles.text}>
                <span className={styles.title}>{s.title}</span>
                {s.term && <span className={styles.term}>{s.term}</span>}
              </span>
              <span className={styles.number} aria-hidden="true">
                {i + 1}
              </span>
            </button>
          </li>
        )
      })}
    </ol>
  )
}
