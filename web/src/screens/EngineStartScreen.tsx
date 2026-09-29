import { Check, RotateCw } from 'lucide-react'
import { useEffect } from 'react'
import InfoTip from '../components/InfoTip'
import { pending, useCheckStream } from '../checks'
import styles from './EngineStartScreen.module.css'

// 서버 EngineStarter와 같은 순서: N.I.N.A. 켜기 → N.I.N.A. 연결(Advanced API 응답)
// 원인이 달라 서버는 둘을 나눠 확인하지만, 화면에서는 한 동작(엔진 켜기)으로 보여 준다.
const STEPS = [pending('launch', 'N.I.N.A. 실행'), pending('connect', 'N.I.N.A. 연결')]

/**
 * 1단계: 화면 가운데 원형 진행 표시 안에 지금 하는 일 한 줄만.
 * 설명 영역은 두지 않는다. 연결되면 잠깐 보여 주고 바로 다음 단계로 (누를 필요 없는 "다음"은 두지 않음).
 */
export default function EngineStartScreen({ onContinue }: { onContinue: () => void }) {
  const { items, allPass, failed, done, restart } = useCheckStream('/api/engine/start', STEPS)
  const [launch, connect] = items
  const problem = items.find((i) => i.status === 'Fail')

  useEffect(() => {
    if (!allPass) return
    const t = setTimeout(onContinue, 900)
    return () => clearTimeout(t)
  }, [allPass, onContinue])

  const state = allPass ? 'done' : done && failed ? 'failed' : 'working'
  const line =
    state === 'done'
      ? 'N.I.N.A.와 연결되었습니다'
      : state === 'failed'
        ? (problem?.message ?? '연결하지 못했습니다')
        : launch.status !== 'Pass'
          ? 'N.I.N.A. 실행 중입니다'
          : connect.status !== 'Pass'
            ? 'N.I.N.A. 연결 중입니다'
            : 'N.I.N.A. 확인 중입니다'

  return (
    <main className={styles.stage}>
      <div className={styles.ring} data-state={state} role="status" aria-live="polite">
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
          <p className={styles.line}>{line}</p>
        </div>
      </div>

      {/* 실패했을 때만: 해결 방법(?)과 다시 시도. 자리는 항상 확보 */}
      <div className={styles.actions} data-shown={state === 'failed'}>
        {problem?.diagnosis?.fix && <InfoTip label="해결 방법" text={problem.diagnosis.fix} />}
        <button type="button" className={styles.retry} onClick={restart} disabled={state !== 'failed'}>
          <RotateCw strokeWidth={2} aria-hidden="true" />
          다시 시도
        </button>
      </div>
    </main>
  )
}
