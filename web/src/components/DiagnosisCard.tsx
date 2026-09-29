import Button from './Button'
import styles from './DiagnosisCard.module.css'

export interface DiagnosisData {
  causes: string[]
  fix: string
  actionLabel?: string | null
  actionUrl?: string | null
  detail?: string | null
}

/**
 * 진단 내용. 형식은 항상 같다 (DESIGN.md 4장):
 * 왜 그런 것 같나요 / 이렇게 해 보세요 / (접힘) 원본.
 * "무슨 일이 있었나요"는 감싸는 영역(StepPanel)의 결과 문장이 맡는다.
 */
export default function DiagnosisBody({ diagnosis, tone }: { diagnosis: DiagnosisData; tone: 'fail' | 'warn' }) {
  return (
    <div className={styles.body} data-tone={tone}>
      <div className={styles.block}>
        <h3>왜 그런 것 같나요</h3>
        <ol className={styles.causes}>
          {diagnosis.causes.map((c) => (
            <li key={c}>{c}</li>
          ))}
        </ol>
      </div>

      <div className={styles.block}>
        <h3>이렇게 해 보세요</h3>
        <p>{diagnosis.fix}</p>
        {diagnosis.actionUrl && (
          <Button className={styles.action} onClick={() => window.open(diagnosis.actionUrl!, '_blank', 'noopener')}>
            {diagnosis.actionLabel ?? '안내 페이지 열기'}
          </Button>
        )}
      </div>

      {diagnosis.detail && (
        <details className={styles.detail}>
          <summary>원본 기록 보기</summary>
          <pre>{diagnosis.detail}</pre>
        </details>
      )}
    </div>
  )
}
