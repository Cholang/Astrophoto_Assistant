import { useEffect, useState } from 'react'
import LiveView from '../components/LiveView'
import PrepCenter from '../components/PrepCenter'
import PrepGuide from '../components/PrepGuide'
import { useRailExtra, type RailItem } from '../components/StepRail'
import { prepareAct, watchPrepare, type PrepTaskStatus, type PrepView } from '../prepare'
import styles from './PrepareScreen.module.css'

/**
 * 촬영 준비 (DESIGN.md 3장 "촬영 준비", 시안 mockups/aa-prepare-arcs-v8.html).
 * 화면 영역: 하늘 화면(배경 전체, LiveView) · 안내(왼쪽 위, PrepGuide) · 중앙 정보(가운데 아래, PrepCenter) · 진행 표시(오른쪽, StepRail).
 * 진행은 서버(Prepare/Flow/PrepareRunner)가 하고, 이 화면은 상태를 받아 그리고 버튼만 전달한다.
 * 사용자가 "촬영 시작"을 누르면(ready) 촬영 단계로.
 */

const RAIL_STATE: Record<PrepTaskStatus, RailItem['state']> = {
  Pending: 'todo',
  NeedsRecheck: 'todo',
  Running: 'now',
  Waiting: 'now',
  Done: 'done',
  Failed: 'problem',
  Skipped: 'skipped',
}

/** [임시] 작업마다 걸어 볼 수 있는 모의 실패 (서버 SimFaults.Known) — 실제 장비(P3)를 붙이면 뺀다 */
const FAULTS: Record<string, [string, string][]> = {
  polar: [['polar.handover', '넘겨받기'], ['polar.stars', '별 찾기'], ['polar.giveback', '돌려주기']],
  calibration: [['calibration.star', '위치 A 별 없음'], ['calibration.measure', '측정']],
  slew: [['slew.low', '대상 낮음'], ['slew.move', '이동']],
  focus: [['focus.stars', '별 없음'], ['focus.stall', '포커서 멈춤']],
  center: [['center.solve', '솔빙']],
  guiding: [['guiding.star', '가이드 별']],
  test: [['test.download', '내려받기'], ['test.bright', '배경 밝음']],
}

export default function PrepareScreen({ onContinue }: { onContinue: () => void }) {
  const [view, setView] = useState<PrepView | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [actError, setActError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  // 사진만 보기 · 9칸 확대 보기 (화면에서만 바꾸는 보기)
  const [peek, setPeek] = useState(false)
  const [grid, setGrid] = useState(false)
  const [faultsOpen, setFaultsOpen] = useState(false)

  useEffect(() => watchPrepare(setView, setProblem), [])

  const cur = view?.current ?? null
  const liveKind = cur?.live?.kind ?? 'none'
  const photo = liveKind === 'test-photo' || liveKind === 'solved-photo'
  // 사진이 바뀌면(다른 작업·다른 사진) 보기를 처음 상태로
  useEffect(() => {
    if (!photo) setPeek(false)
    if (liveKind !== 'test-photo') setGrid(false)
  }, [photo, liveKind])

  const statusProblem = cur?.center.status?.tone === 'Fail'

  // 진행 표시(오른쪽)에 준비의 7작업과 "준비 중단"을 넘긴다. 작업만 — 세부 과정은 넣지 않는다 (2026-10-06 사용자 결정)
  useRailExtra(
    view && view.tasks.length > 0
      ? {
          sky: true,
          items: view.tasks.map((t) => {
            const now = t.id === cur?.taskId
            return {
              id: t.id,
              label: t.title,
              state: now && statusProblem ? 'problem' : RAIL_STATE[t.status],
            }
          }),
          action: { label: '준비 중단', onClick: () => void fetch('/api/prepare/abort', { method: 'POST' }) },
        }
      : { sky: true },
  )

  useEffect(() => {
    if (view?.ready) onContinue()
  }, [view?.ready, onContinue])

  const act = async (action: string) => {
    if (sending) return
    setSending(true)
    setActError(null)
    setActError(await prepareAct(action))
    setSending(false)
  }

  if (problem)
    return (
      <main className={styles.stage}>
        <LiveView current={null} />
        <PrepGuide task="촬영 준비" title="시작하지 못했습니다" text={problem} />
      </main>
    )

  const task = view?.tasks.find((t) => t.id === cur?.taskId)
  const status = actError ? { text: actError, tone: 'Fail' as const } : (cur?.center.status ?? null)

  return (
    <main className={styles.stage} data-peek={peek}>
      <LiveView current={cur} extras={{ grid }} />
      <div className={styles.scrim} />

      {cur && (
        <>
          <div className={styles.over}>
            <PrepGuide task={task?.title ?? ''} title={cur.guide.title} text={cur.guide.text} />
            <PrepCenter readout={cur.center.readout} status={status} actions={cur.center.actions} onAct={(id) => void act(id)} disabled={sending} />
          </div>

          {/* 오른쪽 위: 사진을 볼 때만 */}
          <div className={styles.viewTools}>
            {liveKind === 'test-photo' && !peek && (
              <button type="button" className={styles.tool} onClick={() => setGrid((g) => !g)}>
                {grid ? '사진 전체 보기' : '9칸 확대 보기'}
              </button>
            )}
            {photo && (
              <button type="button" className={styles.tool} onClick={() => setPeek((p) => !p)} aria-pressed={peek}>
                {peek ? '글 다시 보기' : '사진만 보기'}
              </button>
            )}
          </div>

          {/* [임시] 모의 실패 걸기: 다음 동작 하나를 실패시킨다 */}
          <div className={styles.temp}>
            <button type="button" className={styles.tempToggle} onClick={() => setFaultsOpen((o) => !o)} aria-expanded={faultsOpen}>
              [임시] 모의 실패
            </button>
            {faultsOpen && (
              <div className={styles.tempList}>
                {cur.taskId === 'slew' && (
                  <button type="button" onClick={() => void fetch('/api/prepare/sim/ignore-altitude', { method: 'POST' })}>
                    대상이 보인다고 가정
                  </button>
                )}
                {(FAULTS[cur.taskId] ?? []).map(([k, label]) => (
                  <button key={k} type="button" onClick={() => void fetch(`/api/prepare/sim/fail-next/${k}`, { method: 'POST' })}>
                    {label} 실패
                  </button>
                ))}
              </div>
            )}
          </div>
        </>
      )}
    </main>
  )
}
