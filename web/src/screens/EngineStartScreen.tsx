import { pending } from '../checks'
import CheckStepsScreen, { type StepScreenText } from './CheckStepsScreen'

// 서버 EngineStarter와 같은 순서·이름
const STEPS = [pending('launch', 'N.I.N.A. 켜기'), pending('connect', '연결 통로 응답', 'Advanced API')]

const TEXT: StepScreenText = {
  running: ['촬영 엔진을 켜고 있어요', 'N.I.N.A.를 켜고 AA와 연결합니다. 처음 켤 때는 1분쯤 걸릴 수 있습니다.'],
  failed: ['촬영 엔진과 연결하지 못했어요', '아래 안내를 확인한 뒤 다시 시도를 눌러 주세요.'],
  passed: ['촬영 엔진과 연결됐어요', '잠시 후 장비 연결로 넘어갑니다.'],
  retry: '다시 시도',
  stripLabel: '엔진 켜기 단계',
}

/** 1단계: N.I.N.A.를 켜고 연결 통로(Advanced API)가 응답할 때까지 기다린다. */
export default function EngineStartScreen({ onContinue }: { onContinue: () => void }) {
  return <CheckStepsScreen url="/api/engine/start" initial={STEPS} text={TEXT} onContinue={onContinue} />
}
