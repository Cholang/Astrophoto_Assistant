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
  retry: string
  stripLabel: string
}

/**
 * 0단계(설치 확인)와 1단계(엔진 켜기)가 함께 쓰는 화면 틀:
 * 제목·설명 → 가로 쉐브론 띠 → 하단 공통 영역 → 오른쪽 아래 주 버튼 하나.
 */
export default function CheckStepsScreen({
  url,
  initial,
  text,
  onContinue,
}: {
  url: string
  initial: CheckItem[]
  text: StepScreenText
  onContinue: () => void
}) {
  const { items, done, interrupted, failed, allPass, restart, run } = useCheckStream(url, initial)
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
  const index = items.indexOf(current)

  const [title, description] = !done ? text.running : interrupted || failed ? text.failed : text.passed

  return (
    <main className={styles.stage}>
      <div className={styles.content}>
        <div className={styles.intro}>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>

        <StepChevrons steps={items} current={current.id} onSelect={setPicked} label={text.stripLabel} />
        <StepPanel item={current} index={index} total={items.length} />
      </div>

      <footer className={styles.footer}>
        {!done ? (
          <Button variant="primary" disabled>
            다음
          </Button>
        ) : allPass ? (
          <Button variant="primary" onClick={onContinue}>
            다음
          </Button>
        ) : (
          <Button variant="primary" onClick={restart}>
            {text.retry}
          </Button>
        )}
      </footer>
    </main>
  )
}
