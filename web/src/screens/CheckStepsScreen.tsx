import { useEffect, useState } from 'react'
import Button from '../components/Button'
import StepChevrons from '../components/StepChevrons'
import StepPanel from '../components/StepPanel'
import { useCheckStream, type CheckItem } from '../checks'
import styles from './CheckStepsScreen.module.css'

export interface StepScreenText {
  running: [string, string]
  failed: [string, string]
  passed: [string, string]
  stripLabel: string
}

/**
 * 0단계(설치 확인)와 1단계(엔진 켜기)가 함께 쓰는 화면 틀:
 * 제목·설명 → 화면 폭을 채우는 큰 쉐브론 띠(현재 칸 안에 새로고침) → 하단 공통 영역 → 오른쪽 아래 "다음".
 */
export default function CheckStepsScreen({
  url,
  initial,
  text,
  onContinue,
  statusText = (item) => item.message,
  tempComplete,
}: {
  url: string
  initial: CheckItem[]
  text: StepScreenText
  onContinue: () => void
  /** 공통 영역에 보여 줄 상태 문장. 기본은 서버가 보낸 문장 */
  statusText?: (item: CheckItem) => string
  /** [임시] 화면 설계용 버튼: 누르면 선택된 항목 하나를 통과한 것으로 간주. label의 {title}은 항목 이름으로 바뀐다 */
  tempComplete?: { label: string; passMessage: string }
}) {
  const { items, done, interrupted, failed, allPass, restart, run, markPassed } = useCheckStream(url, initial)
  const [picked, setPicked] = useState<string | null>(null)

  // 다시 확인하면 사용자가 고른 칸을 풀고 자동으로 따라가게 한다.
  useEffect(() => setPicked(null), [run])

  // 전부 통과하면 잠깐 보여 주고 자동으로 넘어간다 ("다음"을 눌러도 된다).
  useEffect(() => {
    if (!allPass) return
    const t = setTimeout(onContinue, 1500)
    return () => clearTimeout(t)
  }, [allPass, onContinue])

  // 현재 칸: 사용자가 고른 칸 → 확인 중인 칸 → 첫 실패 칸 → 마지막 칸
  const auto =
    items.find((i) => i.status === 'Running') ??
    items.find((i) => i.status === 'Fail' || i.status === 'Warn') ??
    (done ? items[items.length - 1] : items.find((i) => i.status === 'Pending')) ??
    items[items.length - 1]
  const current = items.find((i) => i.id === picked) ?? auto

  const [title, description] = !done ? text.running : interrupted || failed ? text.failed : text.passed

  return (
    <main className={styles.stage}>
      <div className={styles.intro}>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>

      <StepChevrons
        steps={items}
        current={current.id}
        onSelect={setPicked}
        onRetry={done && !allPass ? restart : undefined}
        label={text.stripLabel}
      />

      <div className={styles.panel}>
        <StepPanel item={current} statusText={statusText(current)} />
      </div>

      <footer className={styles.footer}>
        {/* 지금 선택된 칸 하나만 설치된 것으로 바꾸고, 다음 미설치 칸으로 자동으로 옮겨 간다 */}
        {tempComplete && done && current.status !== 'Pass' && (
          <button
            type="button"
            className={styles.temp}
            onClick={() => {
              markPassed(current.id, tempComplete.passMessage)
              setPicked(null)
            }}
          >
            {tempComplete.label.replace('{title}', current.title)}
          </button>
        )}
        <Button variant="primary" disabled={!allPass} onClick={onContinue}>
          다음
        </Button>
      </footer>
    </main>
  )
}
