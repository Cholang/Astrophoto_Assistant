import { TriangleAlert } from 'lucide-react'
import Button from './Button'
import styles from './NinaLostCard.module.css'

/** Closed = 마무리에서 AA가 일부러 닫음 (경고하지 않음) */
export type NinaState = 'Unknown' | 'Running' | 'Exited' | 'NotResponding' | 'Closed'

/**
 * N.I.N.A.가 꺼지거나 멈췄을 때 화면 위쪽에 붙는 진단 카드 (DESIGN.md 4장: 무슨 일 / 왜 / 이렇게).
 * 전체를 가리는 모달이 아니다. "다시 켜기"를 누르면 1단계 엔진 켜기부터 이어서 장비를 다시 연결한다.
 * 정해 둔 계획은 서버에 그대로 남는다.
 */
export default function NinaLostCard({ state, onRestart }: { state: NinaState; onRestart: () => void }) {
  const hung = state === 'NotResponding'
  return (
    <aside className={styles.card} role="alert">
      <TriangleAlert className={styles.icon} strokeWidth={2} aria-hidden="true" />
      <div className={styles.body}>
        <h2>{hung ? 'N.I.N.A.가 응답하지 않습니다' : 'N.I.N.A.가 꺼졌습니다'}</h2>
        <p>
          {hung
            ? 'N.I.N.A.가 멈춘 것 같습니다. 장비와의 연결도 확인할 수 없습니다.'
            : '장비와의 연결이 끊겼습니다. 사용자가 닫았거나 N.I.N.A.가 멈춰서 꺼졌을 수 있습니다.'}
        </p>
        <p className={styles.fix}>
          {hung
            ? 'N.I.N.A. 창을 닫은 뒤 다시 켜기를 누르면, 장비를 이어서 다시 연결합니다. 정해 둔 계획은 그대로 있습니다.'
            : '다시 켜기를 누르면 N.I.N.A.를 켜고 장비를 이어서 다시 연결합니다. 정해 둔 계획은 그대로 있습니다.'}
        </p>
      </div>
      <Button variant="primary" onClick={onRestart}>
        다시 켜기
      </Button>
    </aside>
  )
}
