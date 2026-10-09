import { TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import styles from './DiagnosticCard.module.css'

/**
 * 진단 카드 (DESIGN.md 4장 — 공통): 화면 위쪽(상태 줄 바로 아래) 가운데에 붙는다. 전체를 가리는 모달이 아니다.
 * 무슨 일(title) / 왜(why, 가능성 높은 순) / 이렇게(fix) + 버튼, 원본 로그는 접어서(raw) — 영문 로그를 먼저 보이지 않는다.
 * tone: fail = 진행이 막힘(N.I.N.A. 꺼짐 등), warn = 알림(N.I.N.A. 오류를 풀어 쓴 것)
 */
export default function DiagnosticCard({
  title,
  why,
  fix,
  raw,
  tone = 'fail',
  actions,
  note,
}: {
  title: string
  why: string | string[]
  fix: string
  raw?: string
  tone?: 'fail' | 'warn'
  actions: ReactNode
  /** 제목 옆 작은 글 (예: "외 2건", "3번") */
  note?: string
}) {
  return (
    <aside className={styles.card} data-tone={tone} role="alert">
      <TriangleAlert className={styles.icon} strokeWidth={2} aria-hidden="true" />
      <div className={styles.body}>
        <h2>
          {title}
          {note && <span className={styles.note}>{note}</span>}
        </h2>
        {Array.isArray(why) ? (
          <ul className={styles.why}>
            {why.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        ) : (
          <p>{why}</p>
        )}
        <p className={styles.fix}>{fix}</p>
        {raw && (
          <details className={styles.raw}>
            <summary>원본 내용</summary>
            <pre>{raw}</pre>
          </details>
        )}
      </div>
      <div className={styles.actions}>{actions}</div>
    </aside>
  )
}
