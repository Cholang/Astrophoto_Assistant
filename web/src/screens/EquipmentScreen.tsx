import { useEffect, useState } from 'react'
import type { CheckItem } from '../checks'
import CheckStepsScreen, { type StepScreenText } from './CheckStepsScreen'

// 장비 이름(X-T5, OnStep …)은 N.I.N.A. 프로필에서 오므로, 서버에 목록을 먼저 물어 칸을 그린다.
const TEXT: StepScreenText = {
  running: ['장비를 연결하고 있어요'],
  failed: ['연결되지 않은 장비가 있어요'],
  passed: ['모든 장비가 연결되었어요'],
  completed: '장비 연결이 끝나 다음 단계로 이동합니다.',
  stripLabel: '장비 연결 항목',
}

const statusText = (item: CheckItem) =>
  item.status === 'Pass'
    ? '연결되어 있습니다'
    : item.status === 'Fail'
      ? '연결되어 있지 않습니다'
      : item.status === 'Running'
        ? '연결하는 중입니다'
        : ''

/**
 * 장비 연결: N.I.N.A. 프로필의 장비를 전원 허브부터 차례로 연결한다.
 * [임시] 지금은 서버 설정 Equipment:Simulate로 모두 연결된 것으로 간주한다 (실제 장비 없이 개발).
 */
export default function EquipmentScreen({ onContinue }: { onContinue: (items: CheckItem[]) => void }) {
  const [plan, setPlan] = useState<CheckItem[] | null>(null)

  useEffect(() => {
    fetch('/api/equipment/plan')
      .then((r) => (r.ok ? r.json() : []))
      .then(setPlan, () => setPlan([]))
  }, [])

  if (plan === null) return null

  return (
    <CheckStepsScreen
      url="/api/equipment/connect"
      initial={plan}
      text={TEXT}
      onContinue={onContinue}
      statusText={statusText}
      recheckUrl={(id) => `/api/equipment/connect/${id}`}
    />
  )
}
