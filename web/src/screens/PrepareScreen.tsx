import { useEffect, useState, type ReactNode } from 'react'
import Button from '../components/Button'
import ProgressLine from '../components/ProgressLine'
import StatusIcon, { type Status } from '../components/StatusIcon'
import StepChevrons, { type ChevronStep } from '../components/StepChevrons'
import { prepareAct, watchPrepare, type PrepStep, type PrepView } from '../prepare'
import styles from './PrepareScreen.module.css'

/** 쉐브론 칸 모양: 통과만 또렷하게. 사용자 차례(Waiting)는 대기 모양 (현재 칸이라 강조색으로 채워진다) */
const CHEVRON: Record<PrepStep['status'], Status> = {
  Pending: 'Pending',
  Running: 'Running',
  Waiting: 'Pending',
  Pass: 'Pass',
  Fail: 'Fail',
  Skipped: 'Skipped',
}

/**
 * 촬영 준비 (DESIGN.md 3장 "촬영 준비", flow.mmd ③). 설치 확인과 같은 틀: 제목 → 쉐브론 7칸 → 하단 공통 영역.
 * 진행은 서버(PrepareRunner)가 하고, 이 화면은 상태를 받아 그리고 버튼만 전달한다.
 * 사용자가 "촬영 시작"을 누르면(ready) 촬영 단계로.
 */
export default function PrepareScreen({ onContinue }: { onContinue: () => void }) {
  const [view, setView] = useState<PrepView | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [picked, setPicked] = useState<string | null>(null)
  const [actError, setActError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  useEffect(() => watchPrepare(setView, setProblem), [])

  // 진행이 다음 칸으로 넘어가면 사용자가 골라 보던 칸을 풀고 따라간다
  useEffect(() => setPicked(null), [view?.current])

  useEffect(() => {
    if (view?.ready) onContinue()
  }, [view?.ready, onContinue])

  if (problem)
    return (
      <main className={styles.stage}>
        <div className={styles.intro}>
          <h1>촬영 준비를 시작하지 못했습니다</h1>
        </div>
        <div className={styles.panelWrap}>
          <section className={styles.panel} data-status="Fail">
            <p className={styles.result}>
              <StatusIcon status="Fail" />
              <span>{problem}</span>
            </p>
          </section>
        </div>
      </main>
    )
  if (!view || view.steps.length === 0) return <main className={styles.stage} />

  const auto = view.steps.find((s) => s.id === view.current) ?? view.steps[0]
  const step = view.steps.find((s) => s.id === picked) ?? auto
  const viewingOther = step.id !== auto.id
  const busy = view.steps.some((s) => s.status === 'Running')

  const act = async (action: string) => {
    if (sending) return
    setSending(true)
    setActError(null)
    setActError(await prepareAct(step.id, action))
    setSending(false)
  }

  const chevrons: ChevronStep[] = view.steps.map((s) => ({ id: s.id, title: s.title, term: s.term, status: CHEVRON[s.status] }))
  const retry = step.status === 'Fail' && step.actions.some((a) => a.id === 'retry') && !viewingOther ? () => void act('retry') : undefined

  return (
    <main className={styles.stage}>
      <div className={styles.intro}>
        <h1>{titleOf(auto)}</h1>
      </div>

      <StepChevrons steps={chevrons} current={step.id} onSelect={setPicked} onRetry={retry} retrying={sending} label="촬영 준비 단계" />

      <div className={styles.panelWrap}>
        <section className={styles.panel} data-status={step.status} aria-live="polite">
          <h2 className={styles.narrowTitle}>{step.title}</h2>
          <p className={styles.hint}>{step.hint}</p>

          {/* 진행 중 표시: 자리는 항상 두고 진행 중일 때만 보인다 */}
          <div className={styles.throbber} data-shown={step.status === 'Running'}>
            <ProgressLine indeterminate label={`${step.title} 진행 중`} />
          </div>

          <p className={styles.result}>
            <StatusIcon status={iconOf(step)} />
            <span>{step.message || (step.status === 'Pending' ? '앞 단계가 끝나면 진행합니다.' : '')}</span>
          </p>

          <Detail step={step} />

          {/* 버튼 줄: 자리는 항상 확보 (버튼이 생겨도 위의 글이 움직이지 않게). 다른 칸을 보는 중이면 버튼 없음 */}
          <div className={styles.actions}>
            {!viewingOther &&
              step.actions
                .filter((a) => !(a.id === 'retry' && retry)) // 다시 시도는 칸 안의 새로고침 아이콘으로
                .map((a) => (
                  <Button key={a.id} variant={a.primary ? 'primary' : 'quiet'} onClick={() => void act(a.id)} disabled={sending || busy}>
                    {a.label}
                  </Button>
                ))}
            <span className={styles.actError} data-shown={!!actError} role="alert">
              {actError}
            </span>
          </div>
        </section>
      </div>

      <TempButtons view={view} step={auto} />
    </main>
  )
}

/** 화면 제목: 지금 진행 중인 칸 기준 (사용자 차례면 할 일, 멈췄으면 멈췄다고) */
function titleOf(s: PrepStep) {
  if (s.status === 'Fail') return '준비가 멈췄습니다'
  if (s.status !== 'Waiting') return '촬영 준비 중입니다'
  switch (s.id) {
    case 'polar':
      return 'SharpCap에서 극축정렬을 해 주세요'
    case 'move':
      return s.actions.some((a) => a.id === 'move') ? '대상으로 이동할까요?' : '대상이 아직 낮습니다'
    case 'focus':
      return '초점을 맞춰 주세요'
    case 'guiding':
      return '가이딩 상태를 확인해 주세요'
    case 'test':
      return '시험 사진을 확인해 주세요'
    default:
      return '확인해 주세요'
  }
}

/** 상태 줄 아이콘. 가이딩 판정이 "지켜보기·문제"이거나 대상이 낮으면 경고 모양 */
function iconOf(s: PrepStep): Status {
  if (s.status === 'Waiting') {
    const verdict = s.detail?.verdict
    if (verdict === 'watch' || verdict === 'bad') return 'Warn'
    if (s.id === 'move' && !s.actions.some((a) => a.id === 'move')) return 'Warn'
    return 'Pending'
  }
  return CHEVRON[s.status]
}

/** 칸마다 추가 정보. 자리는 항상 같은 높이로 확보한다 */
function Detail({ step }: { step: PrepStep }) {
  const d = step.detail ?? {}
  let body: ReactNode = null
  if (step.id === 'guiding' && typeof d.total === 'number')
    body = (
      <span>
        적경 {Number(d.ra).toFixed(1)}″ · 적위 {Number(d.dec).toFixed(1)}″ · {String(d.samples)}번 측정
      </span>
    )
  else if (step.id === 'test' && (step.status === 'Waiting' || step.status === 'Pass'))
    body = typeof d.imageUrl === 'string' ? <img src={d.imageUrl} alt="시험 사진" /> : <span>모의 장비라 사진은 없습니다 (실제 장비에서 여기에 사진이 보입니다)</span>
  return <div className={styles.detail}>{body}</div>
}

/** [임시] 화면 확인용: 다음 동작 하나를 실패시키기, 낮에 대상이 보인다고 가정하기 (오른쪽 아래) */
function TempButtons({ view, step }: { view: PrepView; step: PrepStep }) {
  const lowTarget = step.id === 'move' && step.status === 'Waiting' && step.actions.some((a) => a.id === 'recheck')
  // 실패시킬 칸: 진행 중이거나 곧 진행할 칸 (이동 대기 중이면 이동)
  const next = view.steps.find((s) => s.status === 'Running' || s.status === 'Waiting' || s.status === 'Pending')
  return (
    <div className={styles.tempRow}>
      {lowTarget && (
        <button type="button" className={styles.temp} onClick={() => void fetch('/api/prepare/sim/ignore-altitude', { method: 'POST' })}>
          [임시] 대상이 보인다고 가정
        </button>
      )}
      {next && (
        <button type="button" className={styles.temp} onClick={() => void fetch(`/api/prepare/sim/fail-next/${next.id}`, { method: 'POST' })}>
          [임시] {next.title} 실패시키기
        </button>
      )}
    </div>
  )
}
