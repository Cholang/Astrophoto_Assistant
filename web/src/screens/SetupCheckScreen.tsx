import { pending, type CheckItem } from '../checks'
import CheckStepsScreen, { type StepScreenText } from './CheckStepsScreen'

// 서버 SetupChecker와 같은 순서·이름 (칸에는 설치할 프로그램 이름만).
// 서버가 결과를 보내기 전부터 칸을 그리려고 미리 둔다.
const STEPS = [
  pending('ascom', 'ASCOM Platform'),
  pending('nina', 'N.I.N.A.'),
  pending('advanced-api', 'Advanced API'),
  pending('phd2', 'PHD2'),
  pending('astap', 'ASTAP'),
]

// 제목은 "소프트웨어", 안내 문장은 가장 일상적인 "프로그램"으로 쓴다.
const TEXT: StepScreenText = {
  // 제목 아래 설명은 두지 않는다 — 상태에 따라 한 줄이 생겼다 없어지면 화면 전체가 위아래로 움직인다
  running: ['촬영 준비를 위한 소프트웨어 확인'],
  failed: ['촬영 준비를 위한 소프트웨어 확인'],
  passed: ['필요한 프로그램이 모두 설치되어 있어요'],
  completed: '모든 준비가 완료되어 다음 단계로 이동합니다.',
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
