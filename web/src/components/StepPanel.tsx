import type { CheckItem } from '../checks'
import InfoTip from './InfoTip'
import ProgressLine from './ProgressLine'
import StatusIcon from './StatusIcon'
import styles from './StepPanel.module.css'

/**
 * 쉐브론 띠 아래의 공통 영역. 항목 이름은 쉐브론에 이미 있으므로 다시 쓰지 않는다.
 * 한 줄 설명 → 상태 한 문장(+ 해결 방법 ? 툴팁) → (있으면) 설치 링크.
 */
export default function StepPanel({ item, statusText }: { item: CheckItem; statusText: string }) {
  const problem = item.status === 'Fail' || item.status === 'Warn'
  const how = problem ? item.diagnosis : null

  return (
    <section className={styles.panel} data-status={item.status} aria-live="polite">
      {item.hint && <p className={styles.hint}>{item.hint}</p>}

      {item.status === 'Running' && <ProgressLine indeterminate label={`${item.title} 확인 중`} />}

      {statusText && (
        <p className={styles.result}>
          <StatusIcon status={item.status} />
          <span>{statusText}</span>
          {how?.fix && <InfoTip label="해결 방법" text={how.fix} />}
        </p>
      )}

      {how?.actionUrl && (
        <a className={styles.link} href={how.actionUrl} target="_blank" rel="noreferrer">
          {how.actionLabel ?? '설치 페이지'}
        </a>
      )}
    </section>
  )
}
