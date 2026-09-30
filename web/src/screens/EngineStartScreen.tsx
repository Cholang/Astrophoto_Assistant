import { RotateCw } from 'lucide-react'
import { useEffect } from 'react'
import InfoTip from '../components/InfoTip'
import JarvisRing from '../components/JarvisRing'
import { pending, useCheckStream } from '../checks'
import styles from './EngineStartScreen.module.css'

// 서버 EngineStarter와 같은 순서: N.I.N.A. 켜기 → N.I.N.A. 연결(Advanced API 응답) → 인터넷 연결
// 원인이 달라 서버는 나눠 확인하지만, 화면에서는 한 동작(엔진 켜기)으로 보여 준다.
const STEPS = [pending('launch', 'N.I.N.A. 실행'), pending('connect', 'N.I.N.A. 연결'), pending('internet', '인터넷 연결')]

/**
 * 1단계: 화면 가운데 원형 진행 표시 안에 지금 하는 일 한 줄만.
 * 설명 영역은 두지 않는다. 연결되면 잠깐 보여 주고 바로 다음 단계로 (누를 필요 없는 "다음"은 두지 않음).
 * 다음 단계(장비 연결)는 이 원이 작아지며 가운데에 남는 모양으로 이어진다.
 */
export default function EngineStartScreen({ onContinue }: { onContinue: () => void }) {
  const { items, allPass, failed, done, restart } = useCheckStream('/api/engine/start', STEPS)
  const [launch, connect, internet] = items
  const problem = items.find((i) => i.status === 'Fail')

  useEffect(() => {
    if (!allPass) return
    // 원이 초록으로 닫히고 "준비되었습니다"가 떠오를 때까지 보여 준 뒤 넘어간다
    const t = setTimeout(onContinue, 1900)
    return () => clearTimeout(t)
  }, [allPass, onContinue])

  const state = allPass ? 'done' : done && failed ? 'failed' : 'working'
  const line =
    state === 'done'
      ? '준비되었습니다'
      : state === 'failed'
        ? (problem?.message ?? '연결하지 못했습니다')
        : launch.status !== 'Pass'
          ? 'N.I.N.A. 실행 중입니다'
          : connect.status !== 'Pass'
            ? 'N.I.N.A. 연결 중입니다'
            : internet.status !== 'Pass'
              ? '인터넷 연결 확인 중입니다'
              : '확인 중입니다'

  return (
    <main className={styles.stage}>
      <JarvisRing state={state} className={styles.ring}>
        {line}
      </JarvisRing>

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
