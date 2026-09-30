import { Check, Circle, Minus, TriangleAlert, X } from 'lucide-react'
import styles from './StatusIcon.module.css'

export type Status = 'Pending' | 'Running' | 'Pass' | 'Fail' | 'Warn' | 'Skipped' | 'Absent'

/**
 * 상태는 색만이 아니라 모양으로도 구분한다 (촬영용 적색 테마에서는 모양만 남는다).
 * 통과=체크, 실패=X, 경고=삼각형, 보류=가로줄, 대기=빈 원, 확인 중=반쯤 찬 원
 */
export default function StatusIcon({ status }: { status: Status }) {
  const common = { className: styles.icon, 'data-status': status, 'aria-hidden': true, strokeWidth: 2.25 } as const
  switch (status) {
    case 'Pass':
      return <Check {...common} strokeWidth={2.75} />
    case 'Fail':
      return <X {...common} strokeWidth={2.75} />
    case 'Warn':
      return <TriangleAlert {...common} />
    case 'Skipped':
    case 'Absent':
      return <Minus {...common} />
    case 'Pending':
      return <Circle {...common} />
    case 'Running':
      return (
        <svg {...common} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeLinecap="round">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor" stroke="none" />
        </svg>
      )
  }
}
