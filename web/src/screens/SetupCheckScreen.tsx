import { pending, type CheckItem } from '../checks'
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
  failed: ['먼저 설치할 프로그램이 있어요', '빠진 프로그램을 설치한 뒤 새로고침을 눌러 주세요.'],
  passed: ['필요한 프로그램이 모두 있어요', '잠시 후 촬영 엔진을 켭니다.'],
  stripLabel: '설치 확인 항목',
}

const INSTALLED = '설치되어 있습니다'

// 상태 문장은 두 가지만 쓴다
const statusText = (item: CheckItem) =>
  item.status === 'Pass' ? INSTALLED : item.status === 'Fail' ? '설치되어 있지 않습니다' : item.status === 'Running' ? '확인하는 중입니다' : ''

/** 0단계: 설치 확인. 파일과 레지스트리만 보고 아무것도 실행하지 않는다. */
export default function SetupCheckScreen({ onContinue }: { onContinue: () => void }) {
  return (
    <CheckStepsScreen
      url="/api/setup/check"
      initial={STEPS}
      text={TEXT}
      onContinue={onContinue}
      statusText={statusText}
      // [임시] 화면 설계 중에는 서버가 모두 "설치 안 됨"으로 보고한다 (appsettings Setup:SimulateMissing).
      tempComplete={{ label: '{title} 설치 완료하기 (임시)', passMessage: INSTALLED }}
    />
  )
}
