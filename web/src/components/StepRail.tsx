import { Check, LogOut } from 'lucide-react'
import { createContext, useContext, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { closeApp, inDesktop } from '../host'
import styles from './StepRail.module.css'

/** 촬영 단계 (DESIGN.md "진행 표시", "단계 재구성" 2026-10-07). 연결 = 설치 확인·엔진 켜기·장비 연결 */
export const STAGES = ['연결', '점검', '장비 준비', '대상', '촬영', '마무리'] as const
export type Stage = (typeof STAGES)[number]

/** 지금 단계 아래에 펼치는 작업 한 줄 (예: 장비 준비의 극축 정렬 · 캘리브레이션 · 초점) */
export interface RailItem {
  id: string
  label: string
  state: 'done' | 'now' | 'todo' | 'problem' | 'skipped'
}

/** 화면이 진행 표시에 넘겨주는 것: 지금 단계의 작업 목록과 맨 아래 버튼(예: 준비 중단) */
export interface RailExtra {
  /** 이 작업 목록·버튼이 속한 단계. 화면이 바뀌는 순간 앞 화면의 목록이 새 단계에 붙지 않게, 지금 단계와 같을 때만 쓴다 */
  stage: Stage
  items?: RailItem[]
  action?: { label: string; onClick: () => void }
  /** 화면이 하늘 화면(장비 준비·대상)이면 진행 표시도 하늘 위에 얹는다 */
  sky?: boolean
}

const RailContext = createContext<(extra: RailExtra | null) => void>(() => {})

/** 진행 표시 통로: App이 감싸고, 화면은 useRailExtra로 작업 목록을 넘긴다 */
export function RailProvider({ children, onChange }: { children: ReactNode; onChange: (extra: RailExtra | null) => void }) {
  return <RailContext.Provider value={onChange}>{children}</RailContext.Provider>
}

/** 화면에서: 지금 단계의 작업 목록·버튼을 진행 표시에 보낸다 (화면을 떠나면 지움) */
export function useRailExtra(extra: RailExtra | null) {
  const set = useContext(RailContext)
  const key = (extra?.stage ?? '') + JSON.stringify(extra?.items ?? null) + (extra?.action?.label ?? '') + (extra?.sky ? 'sky' : '')
  useEffect(() => {
    set(extra)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, set])
  useEffect(() => () => set(null), [set])
}

// ── 움직임 (DESIGN.md "진행 표시" — 시안 v9) ─────────
// 점(단계·작업)마다 위(in)·아래(out) 반쪽 선이 있고, 진행 위치까지 초록으로 채운다.
// 앞으로 갈 때: 지난 점에서 다음 점까지 반쪽씩 차례로 채워 선이 한쪽 끝부터 자라고, 선이 닿은 뒤 다음 점이 켜진다.
// 단계를 넘을 때: 새 단계 점에 닿은 뒤 작업 묶음을 접고 펴서 이어 그린다. 뒤로 갈 때: 선을 아래에서 위로 빠르게 거둔다.

interface Node {
  key: string
  stage: number
  item?: RailItem
}

/** 반쪽 선의 기다림·걸리는 시간(ms) */
type Timing = Record<string, [number, number]>

const GROUP_MS = 420
const BACK_MS = 70
const SETTLE_MS = 60

function reducedMotion() {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
}

/** 진행 위치: 지금 단계의 지금(또는 문제) 작업, 없으면 마지막으로 끝난 작업, 그것도 없으면 단계 점 */
function progressKey(stage: number, items: RailItem[] | undefined) {
  if (items?.length) {
    const now = items.findIndex((it) => it.state === 'now' || it.state === 'problem')
    if (now >= 0) return `${stage}:${items[now].id}`
    let last = -1
    items.forEach((it, i) => {
      if (it.state === 'done' || it.state === 'skipped') last = i
    })
    if (last >= 0) return `${stage}:${items[last].id}`
  }
  return `${stage}`
}

/**
 * 진행 표시: 화면 오른쪽 세로 열 (2026-10-05 사용자 결정, 시안 v8·v9). 단계 → (지금 단계의) 작업.
 * 작업은 제목만(결과·세부 과정 없음). 지금 작업만 보이고, 나머지는 마우스를 올리거나 키보드 초점일 때 보인다. 맨 아래 화면 버튼(준비 중단)·앱 끄기.
 * 화면이 바뀌어도 같은 자리·같은 너비. 단계·작업이 바뀌면 상태만 바꿔 부드럽게 넘어간다.
 */
export default function StepRail({ current, extra: given }: { current: Stage; extra?: RailExtra | null }) {
  const now = STAGES.indexOf(current)
  const extra = given?.stage === current ? given : null
  const [open, setOpen] = useState<string | null>(null)

  // 단계마다 마지막으로 받은 작업 목록: 지난 단계의 묶음을 접는 동안, 돌아왔을 때 보이게
  const known = useRef<Record<number, RailItem[]>>({})
  if (extra?.items?.length) known.current[now] = extra.items
  const groups = STAGES.map((_, i): RailItem[] | null => {
    const its = i === now && extra?.items?.length ? extra.items : known.current[i]
    if (!its) return null
    if (i < now) return its.map((it) => ({ ...it, state: it.state === 'skipped' ? 'skipped' : 'done' }))
    if (i > now) return its.map((it) => ({ ...it, state: 'todo' }))
    return its
  })
  const nodes: Node[] = []
  STAGES.forEach((_, i) => {
    nodes.push({ key: `${i}`, stage: i })
    groups[i]?.forEach((item) => nodes.push({ key: `${i}:${item.id}`, stage: i, item }))
  })
  const nodesKey = nodes.map((n) => n.key).join('|')
  const target = progressKey(now, groups[now] ?? undefined)

  // 보이는 상태: 켜진 단계(묶음이 펼쳐진 단계)와 선이 닿은 점. 선 채움은 목표까지 한 번에 정하고 반쪽마다 기다림을 준다
  const [shown, setShown] = useState({ stage: now, at: target })
  const [fill, setFill] = useState<{ upTo: string; timing: Timing }>({ upTo: target, timing: {} })
  const prev = useRef<{ stage: number; key: string } | null>(null)
  const timers = useRef<number[]>([])

  useEffect(() => {
    timers.current.forEach(clearTimeout)
    timers.current = []
    // 화면이 바뀌면 단계와 작업 목록이 잇달아 들어온다: 잠깐 모았다가 한 번에 움직인다
    timers.current.push(window.setTimeout(move, SETTLE_MS))
    function move() {
    const p = prev.current
    prev.current = { stage: now, key: target }
    const keys = nodesKey.split('|')
    const from = p ? keys.indexOf(p.key) : -1
    const to = keys.indexOf(target)
    if (!p || from < 0 || reducedMotion()) {
      setShown({ stage: now, at: target })
      setFill({ upTo: target, timing: {} })
      return
    }
    if (from === to && p.stage === now) return

    const timing: Timing = {}
    const steps: [number, () => void][] = []
    let t = 0
    let openStage = p.stage
    const visible = (n: Node) => !n.item || n.stage === openStage
    const half = (n: Node, side: 'in' | 'out', d: number) => {
      if (visible(n)) {
        timing[`${n.key}/${side}`] = [t, d]
        t += d
      } else timing[`${n.key}/${side}`] = [0, 0]
    }
    if (to > from) {
      const d = Math.max(110, Math.min(240, 1500 / (2 * (to - from))))
      for (let k = from; k < to; k++) {
        const a = nodes[k]
        const b = nodes[k + 1]
        half(a, 'out', d)
        half(b, 'in', d)
        if (!b.item && b.stage !== openStage) {
          // 새 단계 점에 닿음 → 작업 묶음을 바꾸고 이어 그린다
          const s = b.stage
          steps.push([t, () => setShown({ stage: s, at: b.key })])
          openStage = s
          t += GROUP_MS
        }
      }
    } else if (to < from) {
      for (let k = from; k > to; k--) {
        half(nodes[k], 'in', BACK_MS)
        half(nodes[k - 1], 'out', BACK_MS)
      }
    }
    steps.push([t, () => setShown({ stage: now, at: target })])
    setFill({ upTo: target, timing })
    for (const [ms, fn] of steps) {
      if (ms <= 0) fn()
      else timers.current.push(window.setTimeout(fn, ms))
    }
    }
    // nodes는 nodesKey로 대신 본다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, now, nodesKey])
  useEffect(() => () => timers.current.forEach(clearTimeout), [])

  // 작업 묶음 높이: 펼친 묶음만 내용 높이, 나머지 0 (높이를 바꿔야 선이 끊기지 않고 부드럽게 접힌다)
  // 다 펴진 뒤에만 넘침(이름·글로우)을 보인다. 펴짐 타이머는 묶음마다 하나 — 접히면 지운다
  // (지난 타이머가 접힌 묶음을 "다 펴짐"으로 바꾸면 접힌 작업 점이 아래로 쏟아져 보인다)
  const groupRefs = useRef<Record<number, HTMLLIElement | null>>({})
  const settleTimers = useRef<Record<number, number>>({})
  useLayoutEffect(() => {
    for (const [k, el] of Object.entries(groupRefs.current)) {
      if (!el) continue
      const stage = Number(k)
      const isOpen = stage === shown.stage
      const inner = el.firstElementChild as HTMLElement | null
      el.style.height = isOpen && inner ? `${inner.offsetHeight}px` : '0px'
      el.dataset.open = String(isOpen)
      window.clearTimeout(settleTimers.current[stage])
      if (!isOpen) el.dataset.settled = 'false'
      else if (el.dataset.settled !== 'true') {
        const settle = () => {
          if (el.dataset.open === 'true') el.dataset.settled = 'true'
        }
        if (reducedMotion()) settle()
        else settleTimers.current[stage] = window.setTimeout(settle, GROUP_MS + 60)
      }
    }
  }, [shown.stage, nodesKey])
  useEffect(() => () => Object.values(settleTimers.current).forEach((t) => window.clearTimeout(t)), [])

  const index = (key: string) => nodes.findIndex((n) => n.key === key)
  const upTo = index(fill.upTo)
  const at = index(shown.at)
  const line = (n: Node, k: number, side: 'in' | 'out') => {
    const on = side === 'in' ? k <= upTo : k < upTo
    const [dl, dur] = fill.timing[`${n.key}/${side}`] ?? [0, 200]
    return (
      <i
        className={styles.line}
        data-side={side}
        data-on={on}
        style={{ '--dl': `${dl}ms`, '--dur': `${dur}ms` } as CSSProperties}
        aria-hidden="true"
      >
        <b />
      </i>
    )
  }
  const itemState = (n: Node, k: number): RailItem['state'] => {
    const st = n.item!.state
    if (n.stage < shown.stage) return st === 'skipped' ? 'skipped' : 'done'
    if (n.stage > shown.stage) return 'todo'
    // 선이 아직 닿지 않은 지금 작업은 남은 작업처럼
    if ((st === 'now' || st === 'problem') && k > at) return 'todo'
    return st
  }

  return (
    <nav className={styles.rail} aria-label="촬영 진행">
      <ol className={styles.track}>
        {STAGES.map((s, i) => {
          const k = index(`${i}`)
          const state = i < shown.stage ? 'done' : i === shown.stage ? 'now' : 'todo'
          const items = groups[i]
          return [
            <li key={s} className={`${styles.node} ${styles.stage}`} data-state={state} data-first={i === 0} data-last={i === STAGES.length - 1}>
              {line(nodes[k], k, 'in')}
              {line(nodes[k], k, 'out')}
              <span className={styles.name} aria-current={state === 'now' ? 'step' : undefined}>
                {s}
              </span>
              <span className={styles.dot}>
                <Check strokeWidth={2.5} aria-hidden="true" />
              </span>
            </li>,
            items && (
              <li
                key={`${s}-items`}
                className={styles.group}
                ref={(el) => {
                  groupRefs.current[i] = el
                }}
              >
                <ol className={styles.items} aria-label={`${s} 작업`}>
                  {items.map((it) => {
                    const key = `${i}:${it.id}`
                    const ik = index(key)
                    const st = itemState(nodes[ik], ik)
                    return (
                      <li
                        key={key}
                        className={`${styles.node} ${styles.item}`}
                        data-state={st}
                        data-open={open === key}
                        tabIndex={i === shown.stage ? 0 : -1}
                        onMouseEnter={() => setOpen(key)}
                        onMouseLeave={() => setOpen(null)}
                        onFocus={() => setOpen(key)}
                        onBlur={() => setOpen(null)}
                        onClick={() => setOpen(open === key ? null : key)}
                        aria-current={st === 'now' ? 'step' : undefined}
                        aria-label={it.label}
                      >
                        {line(nodes[ik], ik, 'in')}
                        {line(nodes[ik], ik, 'out')}
                        <span className={styles.itemName}>{it.label}</span>
                        <span className={styles.itemDot} />
                      </li>
                    )
                  })}
                </ol>
              </li>
            ),
          ]
        })}
      </ol>
      <div className={styles.end}>
        {extra?.action && (
          <button type="button" className={styles.action} onClick={extra.action.onClick}>
            {extra.action.label}
          </button>
        )}
        {/* 앱 끄기: 전체화면이라 창 닫기 버튼이 없어서 둔다 (데스크톱 창에서만) */}
        {inDesktop() && (
          <button type="button" className={styles.exit} onClick={closeApp} aria-label="앱 끄기" title="앱 끄기">
            <LogOut strokeWidth={2} aria-hidden="true" />
            <span>앱 끄기</span>
          </button>
        )}
      </div>
    </nav>
  )
}
