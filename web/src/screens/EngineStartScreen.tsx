import { pending } from '../checks'
import { PRODUCT } from '../product'
import CheckStepsScreen, { type StepScreenText } from './CheckStepsScreen'

// 서버 EngineStarter와 같은 순서·이름
const STEPS = [pending('launch', 'N.I.N.A. 켜기'), pending('connect', '연결 통로 응답')]

// 설명 줄은 모든 상태에서 같게 두어, 상태가 바뀔 때 화면이 위아래로 움직이지 않게 한다.
const DESCRIPTION = `N.I.N.A.를 켜고 ${PRODUCT.wa} 연결합니다. 처음 켤 때는 1분쯤 걸릴 수 있습니다.`

const TEXT: StepScreenText = {
  running: ['촬영 엔진을 켜고 있어요', DESCRIPTION],
  failed: ['촬영 엔진과 연결하지 못했어요', DESCRIPTION],
  passed: ['촬영 엔진과 연결됐어요', DESCRIPTION],
  completed: '촬영 엔진이 준비되어 다음 단계로 이동합니다.',
  stripLabel: '엔진 켜기 단계',
}

/** 1단계: N.I.N.A.를 켜고 연결 통로(Advanced API)가 응답할 때까지 기다린다. */
export default function EngineStartScreen({ onContinue }: { onContinue: () => void }) {
  return <CheckStepsScreen url="/api/engine/start" initial={STEPS} text={TEXT} onContinue={onContinue} />
}
