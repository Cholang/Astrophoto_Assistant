import { Check } from 'lucide-react'
import type { ReactNode } from 'react'
import styles from './JarvisRing.module.css'

export type RingState = 'working' | 'done' | 'failed'

/**
 * 가운데 원형 진행 표시 (여러 겹의 호가 서로 다른 속도로 돈다).
 * 사용자가 고를 것이 없는 진행 단계에 쓴다 (DESIGN.md 3장). 크기는 감싸는 요소가 정한다.
 * 완료: 회전을 멈추고 바깥 원을 닫아 초록 + 체크. 실패: 회전을 멈추고 오류 색.
 */
export default function JarvisRing({ state, children, className }: { state: RingState; children: ReactNode; className?: string }) {
  return (
    <div className={[styles.ring, className].filter(Boolean).join(' ')} data-state={state} role="status" aria-live="polite">
      <svg className={styles.arcs} viewBox="0 0 200 200" aria-hidden="true">
        <circle className={styles.track} cx="100" cy="100" r="92" />
        <circle className={styles.outer} cx="100" cy="100" r="92" pathLength="100" />
        <circle className={styles.middle} cx="100" cy="100" r="80" pathLength="100" />
        <circle className={styles.inner} cx="100" cy="100" r="70" pathLength="100" />
      </svg>
      <div className={styles.center}>
        {/* 아이콘 자리는 항상 두고 상태에 따라 보이기만 바꾼다 (글자가 움직이지 않게) */}
        <span className={styles.icon}>
          <Check strokeWidth={2.5} aria-hidden="true" data-shown={state === 'done'} />
        </span>
        <div className={styles.line}>{children}</div>
      </div>
    </div>
  )
}
