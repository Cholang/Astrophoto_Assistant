import styles from './StatusIcon.module.css'

export type Status = 'Pending' | 'Running' | 'Pass' | 'Fail' | 'Warn' | 'Skipped'

/**
 * 상태는 색만이 아니라 모양으로도 구분한다 (촬영용 적색 테마에서는 모양만 남는다).
 * 통과=체크, 실패=X, 경고=삼각형, 보류=가로줄, 대기=빈 원, 확인 중=반쯤 찬 원
 */
export default function StatusIcon({ status }: { status: Status }) {
  return (
    <svg className={styles.icon} data-status={status} viewBox="0 0 20 20" aria-hidden="true">
      {status === 'Pass' && <path d="M4.5 10.5l3.5 3.5 7.5-8" />}
      {status === 'Fail' && <path d="M5.5 5.5l9 9M14.5 5.5l-9 9" />}
      {status === 'Warn' && (
        <>
          <path d="M10 3.5l7 12.5H3z" />
          <path d="M10 8.5v3.5M10 14v.01" />
        </>
      )}
      {status === 'Skipped' && <path d="M5 10h10" />}
      {status === 'Pending' && <circle cx="10" cy="10" r="6" />}
      {status === 'Running' && (
        <>
          <circle cx="10" cy="10" r="6" />
          <path d="M10 4a6 6 0 0 1 0 12z" className={styles.fill} />
        </>
      )}
    </svg>
  )
}
