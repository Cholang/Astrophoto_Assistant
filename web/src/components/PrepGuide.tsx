import styles from './PrepGuide.module.css'

/**
 * 안내 (DESIGN.md "촬영 준비" — 왼쪽 위): 제목 줄 "작업 — 세부 과정(또는 결과 말)" + 안내 문장.
 * 숫자는 넣지 않는다(숫자는 중앙 정보에만). 문장 줄 높이는 두 줄을 늘 확보.
 */
export default function PrepGuide({ task, title, text, problem }: { task: string; title: string; text: string; problem?: boolean }) {
  return (
    <section className={styles.guide} aria-live="polite">
      <div className={styles.titleRow}>
        <span className={styles.task}>{task}</span>
        <i className={styles.dash} aria-hidden="true">—</i>
        <h1 className={styles.title}>{title}</h1>
      </div>
      <p className={styles.text} data-problem={problem}>
        {text}
      </p>
    </section>
  )
}
