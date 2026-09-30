import { useEffect, useState, type CSSProperties, type Dispatch, type ReactNode, type SetStateAction } from 'react'
import Button from '../components/Button'
import StepChevrons from '../components/StepChevrons'
import StepPanel from '../components/StepPanel'
import { useCheckStream, type CheckItem } from '../checks'
import styles from './CheckStepsScreen.module.css'

/** [임시] 화면 설계용 버튼을 화면마다 따로 그릴 때 쓰는 손잡이 */
/** 새로고침 아이콘이 적어도 도는 시간 (한 바퀴) */
const RETRY_SPIN_MS = 700
/** "다음"을 누르면 쉐브론이 왼쪽부터 차례로 사라진다: 칸 사이 간격 · 한 칸이 사라지는 시간 */
const LEAVE_STEP_MS = 110
const LEAVE_FADE_MS = 320

export interface TempApi {
  items: CheckItem[]
  done: boolean
  patch: Dispatch<SetStateAction<CheckItem[]>>
}

export interface StepScreenText {
  /** [제목, 설명]. 설명은 비워 둘 수 있다 */
  running: [string, string?]
  failed: [string, string?]
  passed: [string, string?]
  /** 모두 통과했을 때 공통 영역 자리에 보여 줄 문장. 바로 아래에 "다음" 버튼 */
  completed: string
  stripLabel: string
}

/**
 * 0단계(설치 확인)와 1단계(엔진 켜기)가 함께 쓰는 화면 틀:
 * 제목(·설명) → 화면 폭을 채우는 큰 쉐브론 띠(현재 칸 안에 새로고침) → 하단 공통 영역
 * → 모두 통과하면 가운데 아래에 "다음" (눌러야 넘어간다).
 */
export default function CheckStepsScreen({
  url,
  initial,
  text,
  onContinue,
  statusText = (item) => item.message,
  tempComplete,
  recheckUrl,
  restartWhen,
  temp,
}: {
  url: string
  initial: CheckItem[]
  text: StepScreenText
  /** "다음"을 누르면 최종 결과와 함께 부른다 */
  onContinue: (items: CheckItem[]) => void
  /** 공통 영역에 보여 줄 상태 문장. 기본은 서버가 보낸 문장 */
  statusText?: (item: CheckItem) => string
  /** [임시] 화면 설계용 버튼: 누르면 선택된 항목 하나를 통과한 것으로 간주. label의 {title}은 항목 이름으로 바뀐다 */
  tempComplete?: { label: string; passMessage: string }
  /** 있으면 새로고침이 현재 항목 하나만 다시 확인한다. 없으면 전체를 처음부터 (차례가 중요한 단계) */
  recheckUrl?: (id: string) => string
  /** 이 항목의 새로고침은 처음부터 다시 (예: 전원 허브 — 뒤 장비가 허브를 기다리고 있음) */
  restartWhen?: (item: CheckItem) => boolean
  /** [임시] 화면 설계용 버튼을 화면이 직접 그린다 (오른쪽 아래) */
  temp?: (api: TempApi) => ReactNode
}) {
  const { items, done, interrupted, failed, allPass, restart, run, markPassed, recheckOne, patch } = useCheckStream(url, initial)
  const [picked, setPicked] = useState<string | null>(null)
  const [checking, setChecking] = useState(false) // 한 항목 재확인 중 (새로고침 아이콘이 돈다)
  const [leaving, setLeaving] = useState(false) // "다음"을 눌러 떠나는 중

  // 다시 확인하면 사용자가 고른 칸을 풀고 자동으로 따라가게 한다.
  useEffect(() => setPicked(null), [run])

  // 현재 칸: 사용자가 고른 칸 → 확인 중인 칸 → 첫 실패 칸 → 마지막 칸
  const auto =
    items.find((i) => i.status === 'Running') ??
    items.find((i) => i.status === 'Fail' || i.status === 'Warn') ??
    (done ? items[items.length - 1] : items.find((i) => i.status === 'Pending')) ??
    items[items.length - 1]
  const current = items.find((i) => i.id === picked) ?? auto

  const [title, description] = !done ? text.running : interrupted || failed ? text.failed : text.passed

  if (!current) return <main className={styles.stage} />

  // 새로고침: 현재 칸이 확인을 마쳤는데 통과하지 못했을 때만. 가능하면 그 항목 하나만 다시 확인한다.
  // 한 항목 재확인은 조용히: 아래 공통 영역은 결과가 바뀔 때만 바뀌고, 확인하는 동안은 새로고침 아이콘만 돈다
  const canRetry = done && !allPass && (current.status === 'Fail' || current.status === 'Warn')
  const retry =
    recheckUrl && !restartWhen?.(current)
      ? async () => {
          if (checking) return
          setChecking(true)
          const started = performance.now()
          await recheckOne(current.id, recheckUrl(current.id), true)
          // 아이콘이 적어도 한 바퀴는 돌게 (눌렀다는 것이 보이게)
          await new Promise((r) => setTimeout(r, Math.max(0, RETRY_SPIN_MS - (performance.now() - started))))
          setChecking(false)
        }
      : restart

  // "다음": 완료 문장·버튼은 바로 사라지고, 쉐브론이 왼쪽부터 차례로 흐려지며 사라진 뒤 다음 단계로
  const leave = () => {
    if (leaving) return
    setLeaving(true)
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    const total = reduce ? 0 : (items.length - 1) * LEAVE_STEP_MS + LEAVE_FADE_MS + 80
    setTimeout(() => onContinue(items), total)
  }

  return (
    <main
      className={styles.stage}
      data-leaving={leaving}
      style={{ '--leave-step': `${LEAVE_STEP_MS}ms`, '--leave-fade': `${LEAVE_FADE_MS}ms` } as CSSProperties}
    >
      <div className={styles.intro}>
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>

      <StepChevrons
        steps={items}
        // 모두 통과하면 선택 상태를 풀어, 모든 칸이 같은 "확인됨" 모양으로 보이게 한다
        current={allPass ? '' : current.id}
        onSelect={setPicked}
        onRetry={canRetry ? retry : undefined}
        retrying={checking}
        leaving={leaving}
        // 모두 통과하면 칸은 더 고를 수 없고, 초점은 "다음"에만 간다
        disabled={allPass}
        label={text.stripLabel}
      />

      {/* 모두 통과하면 공통 영역 자리에 완료 문장과 바로 아래 "다음" (눌러야 넘어간다) */}
      <div className={styles.panel}>
        {allPass ? (
          <div className={styles.complete}>
            <p>{text.completed}</p>
            <Button variant="primary" onClick={leave} autoFocus>
              다음
            </Button>
          </div>
        ) : (
          <StepPanel item={current} statusText={statusText(current)} />
        )}
      </div>

      {/* 지금 선택된 칸 하나만 설치된 것으로 바꾸고, 다음 미설치 칸으로 자동으로 옮겨 간다 */}
      {tempComplete && done && current.status !== 'Pass' && (
        <div className={styles.tempRow}>
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
        </div>
      )}

      {temp && <div className={styles.tempRow}>{temp({ items, done, patch })}</div>}
    </main>
  )
}
