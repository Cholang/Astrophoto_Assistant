import { pending } from '../checks'
import CheckStepsScreen, { type StepScreenText } from './CheckStepsScreen'

// 서버 SetupChecker와 같은 순서·이름. 서버가 결과를 보내기 전부터 칸을 그리려고 미리 둔다.
const STEPS = [
  pending('ascom', '장비 드라이버 기반', 'ASCOM Platform'),
  pending('nina', '촬영 엔진', 'N.I.N.A.'),
  pending('advanced-api', 'N.I.N.A. 연결 통로', 'Advanced API'),
  pending('phd2', '가이딩 프로그램', 'PHD2'),
  pending('astap', '별 위치 분석', 'ASTAP'),
]

const TEXT: StepScreenText = {
  running: ['이 PC에 필요한 프로그램이 있는지 볼게요', '촬영에 쓰는 프로그램 다섯 가지가 설치되어 있는지 확인합니다.'],
  failed: ['먼저 설치할 프로그램이 있어요', '빠진 프로그램을 설치한 뒤 다시 확인을 눌러 주세요.'],
  passed: ['필요한 프로그램이 모두 있어요', '잠시 후 촬영 엔진을 켭니다.'],
  retry: '다시 확인',
  stripLabel: '설치 확인 항목',
}

/** 0단계: 설치 확인. 파일과 레지스트리만 보고 아무것도 실행하지 않는다. */
export default function SetupCheckScreen({ onContinue }: { onContinue: () => void }) {
  return <CheckStepsScreen url="/api/setup/check" initial={STEPS} text={TEXT} onContinue={onContinue} />
}
