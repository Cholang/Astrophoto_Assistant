import { AlertTriangle, Check, RotateCw } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import InfoTip from '../components/InfoTip'
import JarvisRing from '../components/JarvisRing'
import { useCheckStream, type CheckItem } from '../checks'
import styles from './EquipmentScreen.module.css'

/** 전원 허브: 다른 장비에 전원을 주므로, 허브가 실패하면 나머지는 보류하고 허브부터 해결한다 (서버와 같은 규칙) */
const HUB = 'switch'
const WAITING_FOR_HUB = '전원 허브가 연결되면 확인합니다'

// 크기 (그래프 영역의 기준 길이 = 1). 기준 길이는 높이와 너비×0.75 중 작은 쪽
const CENTER = 0.38 // 가운데 원 지름
const NODE = 0.23 // 장비 원 기본 지름
const SIZE_SPREAD = 0.27 // 장비 원 크기 편차 (±27%)
const ANGLE_JITTER = 14 // 각도 흔들림 (±도). 정다각형처럼 보이지 않게
const RADIUS_JITTER = 0.08 // 궤도 거리 흔들림 (±8%)
const GAP = 14 // 원끼리 최소 간격 (px)
const LINK_MIN = 40 // 가운데 원과 장비 원 사이 최소 간격 (px). 연결 선이 보일 만큼

/**
 * 2단계 장비 연결: 1단계의 원형 표시가 작아져 가운데에 남고, 장비 원들이 방사형으로 둘러싼다.
 * 연결에 성공한 장비는 가운데와 선으로 이어진다. 모든 장비가 필수.
 * [임시] 지금은 서버 설정 Equipment:Simulate로 모두 연결된 것으로 간주한다 (실제 장비 없이 개발).
 */
export default function EquipmentScreen({ onContinue }: { onContinue: (items: CheckItem[]) => void }) {
  const [plan, setPlan] = useState<CheckItem[] | null>(null)

  useEffect(() => {
    fetch('/api/equipment/plan')
      .then((r) => (r.ok ? r.json() : []))
      .then(setPlan, () => setPlan([]))
  }, [])

  if (plan === null) return <main className={styles.stage} />
  return <EquipmentGraph plan={plan} onContinue={onContinue} />
}

function EquipmentGraph({ plan, onContinue }: { plan: CheckItem[]; onContinue: (items: CheckItem[]) => void }) {
  const { items, done, allPass, failed, restart, recheckOne, patch } = useCheckStream('/api/equipment/connect', plan)
  const [picked, setPicked] = useState<string | null>(null)
  const ringBox = useRef<HTMLDivElement>(null)
  const graphRef = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ w: 0, h: 0 })
  const [shrinkFrom, setShrinkFrom] = useState<number | null>(null)

  // 그래프 영역 크기를 재서 원들의 자리를 계산한다 (창 크기가 바뀌면 다시)
  useLayoutEffect(() => {
    const el = graphRef.current
    if (!el) return
    const measure = () => setBox((b) => (b.w === el.clientWidth && b.h === el.clientHeight ? b : { w: el.clientWidth, h: el.clientHeight }))
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // 1단계의 원(clamp(240px, 34vh, 340px)) 크기에서 지금 크기로 바뀌는 것처럼 시작한다 (처음 한 번만)
  useLayoutEffect(() => {
    if (shrinkFrom !== null) return
    const w = ringBox.current?.offsetWidth
    if (!w) return
    const engine = Math.min(340, Math.max(240, window.innerHeight * 0.34))
    setShrinkFrom(engine / w)
  }, [box, shrinkFrom])

  // 모두 연결되면 선이 다 이어진 모습을 잠깐 보여 주고 바로 다음 단계로 (누를 필요 없는 "다음"은 두지 않음)
  useEffect(() => {
    if (!allPass) return
    const t = setTimeout(() => onContinue(items), 1600)
    return () => clearTimeout(t)
  }, [allPass, items, onContinue])

  const state = allPass ? 'done' : done && failed ? 'failed' : 'working'
  const missing = items.filter((i) => i.status === 'Fail').length
  const hubFailed = items.some((i) => i.id === HUB && i.status === 'Fail')
  const centerLine =
    state === 'done'
      ? '모든 장비가 연결되었습니다'
      : state === 'failed'
        ? hubFailed
          ? '전원 허브를 먼저 연결해 주세요'
          : `연결되지 않은 장비가 ${missing}개 있습니다`
        : '장비 연결 중입니다'

  // 아래 상태 줄에 보여 줄 장비: 고른 장비 → 첫 실패 장비
  const selected =
    items.find((i) => i.id === picked && i.status !== 'Pass') ?? items.find((i) => i.status === 'Fail')

  const retry = async (item: CheckItem) => {
    setPicked(null)
    if (item.id !== HUB) {
      void recheckOne(item.id, `/api/equipment/connect/${item.id}`)
      return
    }
    // 허브: 먼저 허브만 다시 연결해 보고, 성공하면 처음부터 이어서 보류된 장비들을 연결한다
    patch((prev) => prev.map((i) => (i.id === HUB ? { ...i, status: 'Running' as const, diagnosis: null } : i)))
    try {
      const res = await fetch(`/api/equipment/connect/${HUB}`)
      const hub = res.ok ? ((await res.json()) as CheckItem) : null
      if (hub?.status === 'Pass') restart()
      else if (hub) patch((prev) => prev.map((i) => (i.id === HUB ? hub : i)))
    } catch {
      patch((prev) => prev.map((i) => (i.id === HUB ? { ...i, status: 'Fail' as const } : i)))
    }
  }

  const geo = layout(items.map((i) => i.id), box.w, box.h)

  return (
    <main className={styles.stage}>
      <div ref={graphRef} className={styles.graph}>
        {geo && (
          <>
            {/* 연결 선: 가운데 원 가장자리 → 장비 원 가장자리. 연결에 성공하면 그려진다 */}
            <svg className={styles.links} viewBox={`0 0 ${box.w} ${box.h}`} aria-hidden="true">
              {items.map((item, i) => {
                const n = geo.nodes[i]
                const dx = n.x - geo.cx
                const dy = n.y - geo.cy
                const len = Math.hypot(dx, dy)
                const ux = dx / len
                const uy = dy / len
                return (
                  <line
                    key={item.id}
                    className={styles.link}
                    data-on={item.status === 'Pass'}
                    x1={geo.cx + ux * (geo.cd / 2 + 6)}
                    y1={geo.cy + uy * (geo.cd / 2 + 6)}
                    x2={n.x - ux * (n.d / 2 + 5)}
                    y2={n.y - uy * (n.d / 2 + 5)}
                    pathLength={1}
                  />
                )
              })}
            </svg>

            <div
              ref={ringBox}
              className={styles.center}
              style={{ left: geo.cx, top: geo.cy, width: geo.cd, '--shrink-from': shrinkFrom, '--cd': `${geo.cd}px` } as CSSProperties}
            >
              <JarvisRing state={state} className={styles.ring}>
                {centerLine}
              </JarvisRing>
            </div>

            {items.map((item, i) => {
              const n = geo.nodes[i]
              return (
                <button
                  key={item.id}
                  type="button"
                  className={styles.node}
                  data-status={item.status}
                  data-selected={selected?.id === item.id}
                  style={{ left: n.x - n.d / 2, top: n.y - n.d / 2, width: n.d, '--d': `${n.d}px`, '--i': i } as CSSProperties}
                  onClick={() => setPicked(item.id)}
                  aria-label={`${item.title} ${item.term ?? ''}: ${statusText(item)}`}
                >
                  <span className={styles.kind}>{item.title}</span>
                  {item.term && <span className={styles.name}>{item.term}</span>}
                  {/* 상태 아이콘 자리는 항상 둔다 (글자가 움직이지 않게) */}
                  <span className={styles.mark} aria-hidden="true">
                    {item.status === 'Pass' && <Check strokeWidth={2.5} />}
                    {item.status === 'Fail' && <AlertTriangle strokeWidth={2} />}
                  </span>
                </button>
              )
            })}
          </>
        )}
      </div>
      {/* 아래 상태 줄: 연결 안 된 장비의 상태·해결 방법·다시 시도. 자리는 항상 확보 */}
      <div className={styles.status} data-shown={!!selected && done}>
        {selected && (
          <>
            <span className={styles.statusName}>
              {selected.title}
              {selected.term && <small>{selected.term}</small>}
            </span>
            <span className={styles.statusText} data-status={selected.status}>
              {statusText(selected)}
            </span>
            {selected.diagnosis?.fix && <InfoTip label="해결 방법" text={selected.diagnosis.fix} />}
            {selected.status === 'Fail' && (
              <button type="button" className={styles.retry} onClick={() => retry(selected)}>
                <RotateCw strokeWidth={2} aria-hidden="true" />
                다시 연결
              </button>
            )}
          </>
        )}
      </div>

      <TempFailButtons items={items} done={done} patch={patch} />
    </main>
  )
}

function statusText(item: CheckItem) {
  switch (item.status) {
    case 'Pass':
      return '연결되어 있습니다'
    case 'Fail':
      return '연결되어 있지 않습니다'
    case 'Running':
      return '연결하는 중입니다'
    case 'Skipped':
      return '전원 허브를 먼저 연결해 주세요'
    default:
      return '연결 대기 중입니다'
  }
}

interface Geo {
  cx: number
  cy: number
  /** 가운데 원 지름 */
  cd: number
  nodes: { x: number; y: number; d: number }[]
}

/**
 * 원들의 자리와 크기 (px). 정다각형처럼 보이지 않게:
 * - 옆으로 약간 넓은 타원 궤도 위에, 각도·거리·크기를 장비마다 조금씩 흔든다
 * - 흔든 값은 장비 id로 정해지므로 다시 열어도 같은 모양 (레이아웃 안정성)
 * - 원끼리·가운데 원과 겹치면 서로 밀어내고, 그래도 안 되면 전체를 조금씩 줄여 맞춘다
 */
function layout(ids: string[], w: number, h: number): Geo | null {
  if (w < 50 || h < 50 || ids.length === 0) return null
  const cx = w / 2
  const cy = h / 2
  const base = Math.min(h, w * 0.75)

  for (let scale = 1; scale > 0.4; scale *= 0.94) {
    const cd = base * CENTER * scale
    const sizes = ids.map((id) => base * NODE * scale * (1 + (seeded(id, 1) * 2 - 1) * SIZE_SPREAD))
    const maxR = Math.max(...sizes) / 2
    const ry = h / 2 - maxR - 8
    const rx = Math.min(w / 2 - maxR - 8, ry * 1.5)
    if (ry <= cd / 2) continue

    const nodes = ids.map((id, i) => {
      const angle = ((-90 + (i * 360) / ids.length + (seeded(id, 2) * 2 - 1) * ANGLE_JITTER) * Math.PI) / 180
      const f = 1 + (seeded(id, 3) * 2 - 1) * RADIUS_JITTER
      return { x: cx + Math.cos(angle) * rx * f, y: cy + Math.sin(angle) * ry * f, d: sizes[i] }
    })

    // 겹침 풀기: 몇 번 반복해 서로 밀어낸다 (정해진 순서라 결과는 항상 같다)
    for (let pass = 0; pass < 60; pass++) {
      let moved = false
      for (const n of nodes) {
        // 가운데 원과 겹치면 바깥쪽으로
        const dx = n.x - cx
        const dy = n.y - cy
        const dist = Math.hypot(dx, dy) || 1
        const need = cd / 2 + n.d / 2 + LINK_MIN
        if (dist < need) {
          n.x = cx + (dx / dist) * need
          n.y = cy + (dy / dist) * need
          moved = true
        }
      }
      for (let a = 0; a < nodes.length; a++)
        for (let b = a + 1; b < nodes.length; b++) {
          const p = nodes[a]
          const q = nodes[b]
          const dx = q.x - p.x
          const dy = q.y - p.y
          const dist = Math.hypot(dx, dy) || 1
          const need = p.d / 2 + q.d / 2 + GAP
          if (dist < need) {
            const push = (need - dist) / 2
            p.x -= (dx / dist) * push
            p.y -= (dy / dist) * push
            q.x += (dx / dist) * push
            q.y += (dy / dist) * push
            moved = true
          }
        }
      // 영역 밖으로 나가지 않게
      for (const n of nodes) {
        n.x = Math.min(w - n.d / 2 - 4, Math.max(n.d / 2 + 4, n.x))
        n.y = Math.min(h - n.d / 2 - 4, Math.max(n.d / 2 + 4, n.y))
      }
      if (!moved) return { cx, cy, cd, nodes }
    }
  }
  return null
}

/** id로 정해지는 0~1 사이 값 (매번 같은 값) */
function seeded(id: string, salt: number) {
  let h = 2166136261 ^ salt
  for (let i = 0; i < id.length; i++) h = Math.imul(h ^ id.charCodeAt(i), 16777619)
  return ((h >>> 0) % 1000) / 1000
}

/**
 * [임시] 화면 설계용: 장비마다 "연결 실패로 만들기" 버튼.
 * 실패 결과는 서버가 실제 실패와 같은 모양으로 만든다 (Equipment:Simulate일 때만).
 * 되돌리기는 "다시 연결" — 시뮬레이션에서는 다시 연결에 성공한다.
 */
function TempFailButtons({
  items,
  done,
  patch,
}: {
  items: CheckItem[]
  done: boolean
  patch: (fn: (prev: CheckItem[]) => CheckItem[]) => void
}) {
  const fail = async (id: string) => {
    const res = await fetch(`/api/equipment/connect/${id}?simulateFail=true`)
    if (!res.ok) return
    const result = (await res.json()) as CheckItem
    patch((prev) =>
      prev.map((i) =>
        i.id === id
          ? result
          : id === HUB
            ? { ...i, status: 'Skipped' as const, message: WAITING_FOR_HUB, diagnosis: null }
            : i,
      ),
    )
  }

  const connected = items.filter((i) => i.status === 'Pass')
  if (!done || connected.length === 0) return null
  return (
    <div className={styles.temp}>
      <span>연결 실패로 만들기 (임시)</span>
      {connected.map((i) => (
        <button key={i.id} type="button" onClick={() => fail(i.id)}>
          {i.title}
        </button>
      ))}
    </div>
  )
}
