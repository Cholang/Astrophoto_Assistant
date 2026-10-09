import { useEffect, useState } from 'react'
import LiveView from '../components/LiveView'
import PrepCenter from '../components/PrepCenter'
import PrepGuide from '../components/PrepGuide'
import { useRailExtra, type RailItem } from '../components/StepRail'
import { prepareAbort, prepareAct, watchPrepare, type PrepGroup, type PrepTaskStatus, type PrepView } from '../prepare'
import styles from './PrepareScreen.module.css'

/**
 * 장비 준비 · 대상 묶음 화면 (DESIGN.md "단계 재구성", 시안 mockups/aa-prepare-arcs-v9.html). group으로 묶음을 고른다.
 * 화면 영역: 하늘 화면(배경 전체, LiveView) · 안내(왼쪽 위, PrepGuide) · 중앙 정보(가운데 아래, PrepCenter) · 진행 표시(오른쪽, StepRail).
 * 진행은 서버(Prepare/Flow/PrepareRunner 두 개)가 하고, 이 화면은 상태를 받아 그리고 버튼만 전달한다.
 * 끝 버튼(장비 준비: "대상 고르기", 대상: "촬영 시작")을 누르면(ready) onDone. 센터링 뒤 "다른 대상"이면 onReplan,
 * 대상이 장비 준비 작업을 다시 하자고 해 멈춰 두면(handoff) onHandoff.
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
  polar: [['polar.dialog', 'PHD2 장비 연결 창'], ['polar.handover', '넘겨받기'], ['polar.stars', '별 찾기'], ['polar.giveback', '돌려주기']],
  calibration: [['calibration.star', '위치 A 별 없음'], ['calibration.measure', '측정']],
  slew: [['slew.low', '대상 낮음'], ['slew.move', '이동']],
  focus: [['focus.stars', '별 없음'], ['focus.stall', '포커서 멈춤'], ['focus.temp', '기온 3.3°C 내려감 (다음 초점 확인)']],
  center: [['center.solve', '솔빙'], ['center.hfr', '센터링 사진 별 커짐 (다음 초점 확인)']],
  focuscheck: [['focus.stars', '다시 맞출 때 별 없음']],
  guiding: [['guiding.star', '가이드 별'], ['guiding.calibration', '보정값 불일치']],
  flat: [['wrap.bright', '패널 너무 밝음']],
  test: [['test.expose', '노출'], ['test.download', '내려받기'], ['test.bright', '배경 밝음']],
}

/** 대상 묶음의 진행 표시 맨 위 작업: 계획 (계획 화면에서 끝내고 온다) */
const PLAN_ITEM: RailItem = { id: 'plan', label: '계획', state: 'done' }

export default function PrepareScreen({
  group,
  onDone,
  onReplan,
  onHandoff,
}: {
  group: PrepGroup
  onDone: () => void
  onReplan?: () => void
  onHandoff?: (to: PrepGroup) => void
}) {
  const [view, setView] = useState<PrepView | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [actError, setActError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  // 사진만 보기 · 9칸 확대 보기 (화면에서만 바꾸는 보기)
  const [peek, setPeek] = useState(false)
  const [grid, setGrid] = useState(false)
  const [faultsOpen, setFaultsOpen] = useState(false)

  useEffect(() => watchPrepare(group, setView, setProblem), [group])

  const cur = view?.current ?? null
  const liveKind = cur?.live?.kind ?? 'none'
  const photo = liveKind === 'test-photo' || liveKind === 'solved-photo'
  // 사진이 바뀌면(다른 작업·다른 사진) 보기를 처음 상태로
  useEffect(() => {
    if (!photo) setPeek(false)
    if (liveKind !== 'test-photo') setGrid(false)
  }, [photo, liveKind])

  const statusProblem = cur?.center.status?.tone === 'Fail'

  // 진행 표시(오른쪽)에 묶음의 작업과 중단 버튼을 넘긴다. 작업만 — 세부 과정은 넣지 않는다 (2026-10-06 사용자 결정)
  const items: RailItem[] | undefined =
    view && view.tasks.length > 0
      ? view.tasks.map((t) => ({
          id: t.id,
          label: t.title,
          state: t.id === cur?.taskId && statusProblem ? 'problem' : RAIL_STATE[t.status],
        }))
      : undefined
  useRailExtra({
    stage: group === 'rig' ? '장비 준비' : group === 'wrap' ? '마무리' : '대상',
    sky: true,
    items: group === 'target' ? [PLAN_ITEM, ...(items ?? [])] : items,
    // 마무리에는 중단 버튼을 두지 않는다 (건너뛰기는 화면 버튼으로)
    action: group === 'wrap' ? undefined : { label: group === 'rig' ? '준비 중단' : '대상 중단', onClick: () => prepareAbort(group) },
  })

  useEffect(() => {
    if (view?.ready) onDone()
  }, [view?.ready, onDone])

  const handoff = view?.handoff ?? null
  useEffect(() => {
    if (handoff) onHandoff?.(handoff)
  }, [handoff, onHandoff])

  const act = async (action: string) => {
    if (sending) return
    setSending(true)
    setActError(null)
    const err = await prepareAct(group, action)
    setActError(err)
    setSending(false)
    // 다른 단계로 가는 버튼 (센터링 뒤 "다른 대상" → 계획)
    if (!err && action === 'flow:replan') onReplan?.()
  }

  if (problem)
    return (
      <main className={styles.stage}>
        <LiveView current={null} />
        <PrepGuide task={group === 'rig' ? '장비 준비' : '대상'} title="시작하지 못했습니다" text={problem} />
      </main>
    )

  const task = view?.tasks.find((t) => t.id === cur?.taskId)
  const status = actError ? { text: actError, tone: 'Fail' as const } : (cur?.center.status ?? null)

  return (
    <main className={styles.stage} data-peek={peek}>
      <LiveView current={cur} extras={{ grid }} simulated={view?.simulated ?? true} />
      <div className={styles.scrim} />

      {cur && (
        <>
          <div className={styles.over}>
            <PrepGuide task={task?.title ?? ''} title={cur.guide.title} text={cur.guide.text} />
            <PrepCenter readout={cur.center.readout} status={status} actions={cur.center.actions} onAct={(id) => void act(id)} disabled={sending} />
          </div>

          {/* 오른쪽 위: 사진을 볼 때만 */}
          <div className={styles.viewTools}>
            {/* 9칸 확대 보기는 아직 모의 그림만 — 실장비에서는 숨긴다 (실제 사진 9칸 자르기는 저장 형식 확인 뒤, CX-APP-R5) */}
            {liveKind === 'test-photo' && !peek && view?.simulated && (
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

          {/* [임시] 모의 실패 걸기: 다음 동작 하나를 실패시킨다 (모의 장비일 때만) */}
          {view?.simulated && (
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
          )}
        </>
      )}
    </main>
  )
}
