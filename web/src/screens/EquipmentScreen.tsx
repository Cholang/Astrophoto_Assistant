import { AlertTriangle, Check, RotateCw } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import Button from '../components/Button'
import ConfirmDialog from '../components/ConfirmDialog'
import DeviceIcon from '../components/DeviceIcon'
import ScopeList from '../components/ScopeList'
import JarvisRing from '../components/JarvisRing'
import { useCheckStream, type CheckItem } from '../checks'
import { scaleOf } from '../frame'
import { coordText, type CurrentSite } from '../sites'
import engineStyles from './EngineStartScreen.module.css'
import styles from './EquipmentScreen.module.css'

/** 전원 허브: 다른 장비에 전원을 주므로, 허브가 실패하면 나머지는 보류하고 허브부터 해결한다 (서버와 같은 규칙) */
const HUB = 'switch'
const WAITING_FOR_HUB = '전원 허브가 연결되면 확인합니다'

// 크기 (그래프 영역의 기준 길이 = 1). 기준 길이는 높이와 너비×0.75 중 작은 쪽
const CENTER = 0.456 // 가운데 원 지름 (2026-09-30: 0.38의 120%)
const NODE = 0.23 // 장비 원 기본 지름
const SIZE_SPREAD = 0.27 // 장비 원 크기 편차 (±27%)
const SMALL = 0.3 // 등록 안 된 장비의 작은 원: 가장 작은 장비 원의 30%
const EDIT_NODE = 0.14 // 변경 모드의 아이콘 원 지름
const EDIT_CENTER_SCALE = 0.72 // 변경 모드에서 가운데 원 크기 (EquipmentScreen.module.css와 같게)
const ANGLE_JITTER = 14 // 각도 흔들림 (±도). 정다각형처럼 보이지 않게
const RADIUS_JITTER = 0.08 // 궤도 거리 흔들림 (±8%)
// 원끼리 최소 간격: 연결 모드에서 가장 작은 장비 원(글자가 든 원)의 반지름 (2026-09-30 사용자 규칙). connectLayout에서 계산
const LINK_MIN = 40 // 가운데 원과 장비 원 사이 최소 간격 (px). 연결 선이 보일 만큼

// 자동 진행 (DESIGN.md 3장 "장비 연결"): 4초 막대, "장비 변경"은 3.5초까지만 받는다
const ADVANCE_MS = 4000
const CHANGE_UNTIL_MS = 3500
const SLOW_RATE = 0.3 // 변경 버튼을 가리키는 동안 막대가 흐르는 배속
const ABSORB_MS = 960 // 원들이 가운데 원 뒤로 흡수되는 시간·펼쳐지는 시간 (CSS와 같게)
const LIST_ROOM = 310 // 드라이버 목록 폭 280 + 간격 (CSS와 같게)
/** 망원경 (서버 EquipmentConnector.Scope): 드라이버 목록 대신 망원경 목록을 연다 */
const SCOPE = 'scope'

type Mode = 'connect' | 'edit'
/** 화면 모양: 연결 모드 / 가운데로 흡수 중 / 가운데에서 펼쳐지기 직전 / 변경 모드 */
type Phase = 'connect' | 'absorb' | 'spread' | 'edit'

/**
 * 장비 연결: 1단계의 원형 표시가 작아져 가운데에 남고, 장비 원들이 방사형으로 둘러싼다.
 * 연결에 성공한 장비는 가운데와 선으로 이어진다. 등록되지 않은 장비는 아이콘만 있는 작은 원.
 * 모두 되면 4초 막대 뒤 다음 단계로. 그 사이 "장비 변경"을 누르면 변경 모드 — 장비를 등록·변경·제거한다.
 * [임시] 지금은 서버 설정 Equipment:Simulate로 연결을 흉내 낸다 (실제 장비 없이 개발).
 */
/** 장비 연결 화면이 끝나고 넘어갈 때: changeSite면 관측지 고르기로 ("변경"을 누름) */
export type EquipmentDone = (items: CheckItem[], opts: { changeSite: boolean }) => void

/** 막대 위에 보여 줄 지금 관측지 (App이 N.I.N.A.에서 읽어 둔 값). undefined면 아직 모름 */
export interface SiteLine {
  current: CurrentSite | null
  name: string | null
}

export default function EquipmentScreen({ onContinue, site }: { onContinue: EquipmentDone; site?: SiteLine }) {
  const [plan, setPlan] = useState<CheckItem[] | null>(null)
  // 장비 목록을 받는 동안에는 1단계와 똑같은 자리·크기의 원을 그대로 보여 준다 (화면이 바뀌어도 원이 끊기지 않게).
  // 목록이 오면 그 원의 자리에서 가운데 원이 작아지며 옮겨 가도록 자리를 넘긴다.
  const holdRing = useRef<HTMLDivElement>(null)
  const from = useRef<DOMRect | null>(null)
  useLayoutEffect(() => {
    if (plan === null && holdRing.current) from.current = holdRing.current.getBoundingClientRect()
  })

  useEffect(() => {
    fetch('/api/equipment/plan')
      .then((r) => (r.ok ? r.json() : []))
      .then(setPlan, () => setPlan([]))
  }, [])

  if (plan === null)
    return (
      // 1단계 화면을 그대로 이어 보여 주는 자리라 나타나는 효과 없이 바로
      <main className={engineStyles.stage} style={{ animation: 'none' }}>
        <div ref={holdRing}>
          <JarvisRing state="done" className={engineStyles.ring}>
            준비되었습니다
          </JarvisRing>
        </div>
        <div className={engineStyles.actions} aria-hidden="true" />
      </main>
    )
  return <EquipmentGraph plan={plan} onContinue={onContinue} from={from.current} site={site} />
}

function EquipmentGraph({
  plan,
  onContinue,
  from,
  site,
}: {
  plan: CheckItem[]
  onContinue: EquipmentDone
  /** 1단계 원이 있던 자리 (화면 좌표). 가운데 원이 여기서 작아지며 옮겨 온다 */
  from: DOMRect | null
  site?: SiteLine
}) {
  const { items, done, allPass, failed, restart, recheckOne, patch } = useCheckStream('/api/equipment/connect', plan)
  const [picked, setPicked] = useState<string | null>(null)
  const ringBox = useRef<HTMLDivElement>(null)
  const graphRef = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ w: 0, h: 0, left: 0, vw: 0 })
  const [shrinkFrom, setShrinkFrom] = useState<{ scale: number; dx: number; dy: number } | null>(null)

  // 모드: 연결 ↔ 변경. phase는 전환 애니메이션 단계
  const [mode, setMode] = useState<Mode>('connect')
  const [phase, setPhase] = useState<Phase>('connect')
  const changed = useRef(new Set<string>())
  // 변경 모드에서 드라이버 목록을 연 원 (selected)
  const [listKind, setListKind] = useState<string | null>(null)

  // 그래프 영역 크기와 창 안에서의 위치를 재서 원들의 자리를 계산한다 (창 크기가 바뀌면 다시)
  // 변경 모드의 가로 자리는 창 전체 너비 기준이라 그래프의 왼쪽 위치(left)와 창 너비(vw)도 잰다
  useLayoutEffect(() => {
    const el = graphRef.current
    if (!el) return
    const measure = () => {
      // vw: 앱 틀(1920 기준 화면)의 너비. 창 너비가 아니라 틀 너비라 확대·축소와 관계없다
      const frameEl = el.closest<HTMLElement>('[data-frame]')
      const next = { w: el.clientWidth, h: el.clientHeight, left: el.offsetLeft, vw: frameEl?.clientWidth ?? window.innerWidth }
      setBox((b) => (b.w === next.w && b.h === next.h && b.left === next.left && b.vw === next.vw ? b : next))
    }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    window.addEventListener('resize', measure)
    return () => {
      ro.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [])

  // 1단계 원의 자리·크기에서 지금 자리·크기로 옮겨 오는 것처럼 시작한다 (처음 한 번만)
  useLayoutEffect(() => {
    if (shrinkFrom !== null) return
    const el = ringBox.current
    if (!el) return
    const now = el.getBoundingClientRect()
    if (!now.width) return
    // 화면에서 잰 값(확대·축소 적용)을 틀 안의 길이로 바꿔서 옮긴다 — translate는 틀 안에서 다시 확대·축소되므로
    const s = scaleOf(el)
    const frameH = el.closest<HTMLElement>('[data-frame]')?.clientHeight ?? window.innerHeight
    const engine = from ? from.width : Math.min(340, Math.max(240, frameH * 0.34)) * s
    setShrinkFrom({
      scale: engine / now.width,
      dx: from ? (from.left + from.width / 2 - (now.left + now.width / 2)) / s : 0,
      dy: from ? (from.top + from.height / 2 - (now.top + now.height / 2)) / s : 0,
    })
  }, [box, shrinkFrom, from])

  // ── 자동 진행: 모두 되면 4초 막대. 그동안 "장비 변경"을 누르면 변경 모드로 ─────────
  const counting = mode === 'connect' && phase === 'connect' && allPass
  const [late, setLate] = useState(false) // 3.5초가 지났으면 "장비 변경"을 받지 않는다
  // "장비 변경"·관측지 "변경" 버튼을 가리키는(또는 포커스) 동안은 시간이 0.3배로 흐른다 (2026-10-01 사용자 결정, 멈춤 대신)
  const slow = useRef(false)
  const fillRef = useRef<HTMLDivElement>(null)
  const slowProps = {
    onPointerEnter: () => (slow.current = true),
    onPointerLeave: () => (slow.current = false),
    onFocus: () => (slow.current = true),
    onBlur: () => (slow.current = false),
  }
  const elapsed = useRef(0) // 막대가 흐른 시간(배속 반영). 장비 목록이 바뀌어 효과가 다시 돌아도 이어서
  useEffect(() => {
    const fill = fillRef.current
    if (!counting) {
      elapsed.current = 0
      setLate(false)
      if (fill) fill.style.width = '0%'
      return
    }
    // 흐른 시간을 프레임마다 배속을 곱해 더하고, 막대 길이도 같은 값으로 그린다
    let last = performance.now()
    let frame = 0
    const tick = (now: number) => {
      elapsed.current += (now - last) * (slow.current ? SLOW_RATE : 1)
      last = now
      if (fill) fill.style.width = `${Math.min(100, (elapsed.current / ADVANCE_MS) * 100)}%`
      if (elapsed.current >= CHANGE_UNTIL_MS) setLate(true)
      if (elapsed.current >= ADVANCE_MS) return onContinue(items, { changeSite: false })
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [counting, items, onContinue])

  // ── 모드 전환: 원들이 가운데로 흡수됐다가 다른 배치로 펼쳐진다 ─────────
  const [returned, setReturned] = useState(false) // 한 번이라도 모드를 바꿨으면 원이 기다리지 않고 나타난다
  const switchMode = useCallback((next: Mode) => {
    setReturned(true)
    setListKind(null)
    setPicked(null)
    setPhase('absorb')
    setTimeout(
      () => {
        // 새 모드의 원들을 가운데에 놓고 한 번 그린 뒤 제자리로 보내야 펼쳐지는 움직임이 된다
        setMode(next)
        setPhase('spread')
        requestAnimationFrame(() => requestAnimationFrame(() => setPhase(next)))
      },
      reduceMotion() ? 0 : ABSORB_MS,
    )
  }, [])

  const startEdit = () => {
    if (mode !== 'connect' || late) return
    slow.current = false // 버튼이 "변경 완료"로 바뀌어 가리킴이 끝났다는 알림을 못 받으므로
    changed.current.clear()
    switchMode('edit')
  }

  // 변경 완료: 연결 모드로 돌아가 바뀐 장비만 연결한다 (허브가 바뀌면 처음부터)
  const finishEdit = () => {
    if (mode !== 'edit') return
    const kinds = [...changed.current]
    switchMode('connect')
    setTimeout(
      () => {
        if (kinds.includes(HUB)) return restart()
        for (const kind of kinds) {
          const item = items.find((i) => i.id === kind)
          if (item && item.status !== 'Absent') void recheckOne(kind, `/api/equipment/connect/${kind}`)
        }
      },
      reduceMotion() ? 0 : ABSORB_MS,
    )
  }

  // 장비 고르기·제거: 서버에 저장하고 그 원의 칸을 바꾼다
  // 실제 모드에서 N.I.N.A.가 바꾸기·제거에 실패하면 서버는 원래 장비를 그대로 두고 error를 돌려준다 → 그 원 아래에 보여 준다
  const [editError, setEditError] = useState<{ kind: string; message: string } | null>(null)
  const select = async (kind: string, device: { id: string; name: string } | null) => {
    setListKind(null)
    setEditError(null)
    const res = await fetch('/api/equipment/select', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ kind, deviceId: device?.id ?? null, name: device?.name ?? null }),
    })
    if (!res.ok) return
    const { item, error } = (await res.json()) as { item: CheckItem; error: string | null }
    patch((prev) => prev.map((i) => (i.id === kind ? item : i)))
    if (error) setEditError({ kind, message: error })
    else changed.current.add(kind)
  }

  // ── 가운데 문장 ─────────
  // 어느 장비든 연결하는 중이면 '연결 중' (허브를 다시 연결하는 동안 '0개 있습니다'가 먼저 보이지 않게, 개수는 연결이 끝난 뒤에)
  const connecting = items.some((i) => i.status === 'Running')
  const state = allPass ? 'done' : connecting ? 'working' : done && failed ? 'failed' : 'working'
  const without = items.filter((i) => i.status === 'Warn')
  const missing = items.filter((i) => i.status === 'Fail' && i.severity !== 'Optional').length
  const hubFailed = items.some((i) => i.id === HUB && i.status === 'Fail')
  // 고른 원: 연결되지 않은 장비(실패·경고)만 고를 수 있다. 연결되면 저절로 풀린다
  const selected = mode === 'connect' ? items.find((i) => i.id === picked && selectable(i)) : undefined
  const centerLine =
    mode === 'edit'
      ? '바꿀 장비를 골라 주세요'
      : selected
        ? `${selected.title} 연결되지 않음`
        : state === 'done'
          ? without.length === 0
            ? '모든 장비가 연결되었습니다'
            : without.length === 1
              ? `${without[0].title} 없이 진행합니다`
              : `장비 ${without.length}개 없이 진행합니다`
          : state === 'failed'
            ? hubFailed
              ? '전원 허브를\n먼저 연결해 주세요'
              : `연결되지 않은 장비가\n${missing}개 있습니다`
            : '장비 연결 중입니다'

  // 원을 누르면 고르고, 한 번 더 누르면 풀린다
  const togglePick = (item: CheckItem) => {
    if (!selectable(item)) return
    setPicked((p) => (p === item.id ? null : item.id))
  }

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

  // 준필수 장비를 없이 진행: 서버에 기록해 두면 뒤 단계(계획 추천)가 맞춘다
  const goWithout = async (item: CheckItem) => {
    setPicked(null)
    const res = await fetch(`/api/equipment/skip/${item.id}`, { method: 'POST' })
    if (!res.ok) return
    const result = (await res.json()) as CheckItem
    patch((prev) => prev.map((i) => (i.id === item.id ? result : i)))
  }

  // ── 자리 계산 ─────────
  // 자리 계산(200번 섞어 보기·밀어내기)은 장비 목록·크기가 바뀔 때만 — 고르기·마우스 올리기 같은 다른 변화로 다시 계산하지 않게. seed로 정해져 결과는 같다
  const connectGeo = useMemo(() => connectLayout(items, box.w, box.h), [items, box.w, box.h])
  // 0~10 가로 자리는 창 너비 기준이되, 그래프 너비의 1.25배까지만 (울트라와이드에서 그래프 밖으로 나가 가장자리에 한 줄로 몰리지 않게).
  // 그래프는 창 가운데에 있으므로 노트북 크기 창에서는 예전(frac × 창 너비 − 그래프 왼쪽)과 같은 자리
  const editGeo = useMemo(
    () => editLayout(items, box.w, box.h, (frac) => box.w / 2 + (frac - 0.5) * Math.min(box.vw, box.w / 0.8)),
    [items, box.w, box.h, box.vw],
  )
  const geo = mode === 'edit' ? editGeo : connectGeo
  const centerOf = connectGeo

  return (
    <main className={styles.stage}>
      <div ref={graphRef} className={styles.graph} data-phase={phase} data-mode={mode} data-returned={returned}>
        {geo && centerOf && (
          <>
            {/* 연결 선: 가운데 원 가장자리 → 장비 원 가장자리. 연결 모드에서 연결에 성공하면 그려진다 */}
            <svg className={styles.links} viewBox={`0 0 ${box.w} ${box.h}`} aria-hidden="true">
              {phase === 'connect' &&
                items.map((item, i) => {
                  const n = connectGeo!.nodes[i]
                  if (item.status === 'Absent') return null
                  const dx = n.x - connectGeo!.cx
                  const dy = n.y - connectGeo!.cy
                  const len = Math.hypot(dx, dy) || 1
                  const ux = dx / len
                  const uy = dy / len
                  return (
                    <line
                      key={item.id}
                      className={styles.link}
                      data-on={item.status === 'Pass'}
                      x1={connectGeo!.cx + ux * (connectGeo!.cd / 2 + 6)}
                      y1={connectGeo!.cy + uy * (connectGeo!.cd / 2 + 6)}
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
              style={
                {
                  left: centerOf.cx,
                  top: centerOf.cy,
                  width: centerOf.cd,
                  '--shrink-from': shrinkFrom?.scale ?? 1,
                  '--from-dx': `${shrinkFrom?.dx ?? 0}px`,
                  '--from-dy': `${shrinkFrom?.dy ?? 0}px`,
                  '--cd': `${centerOf.cd}px`,
                } as CSSProperties
              }
              data-ready={shrinkFrom !== null}
            >
              {/* 장비 변경·변경 완료는 원 안, 글자 아래 (2026-10-01 사용자 결정) */}
              <JarvisRing
                state={mode === 'edit' ? 'working' : state}
                className={styles.ring}
                action={
                  mode === 'connect' ? (
                    // 막대가 도는 동안 가리키면 시간이 느리게 흐른다. 버튼이 꺼져도 가리킨 상태를 놓치지 않게 감싼 칸에서 받는다
                    <span className={styles.slowZone} {...slowProps}>
                      <Button onClick={startEdit} disabled={!done || late || phase !== 'connect'}>
                        장비 변경
                      </Button>
                    </span>
                  ) : (
                    <Button variant="primary" onClick={finishEdit} disabled={phase !== 'edit'}>
                      변경 완료
                    </Button>
                  )
                }
              >
                {centerLine}
              </JarvisRing>
              {/* 1단계 끝 모습(초록 완료 원)을 위에 겹쳐 두고, 옮겨 오며 작아지는 동안 천천히 흐려진다 → 아래의 진행 중 원으로 크로스페이드.
                  두 원이 같은 틀 안에서 함께 움직여 크기·자리가 어긋나지 않는다. 모드를 바꾼 뒤(returned)에는 없음 */}
              {!returned && (
                <div className={styles.fromRing} aria-hidden="true">
                  <JarvisRing state="done" className={styles.ring}>
                    준비되었습니다
                  </JarvisRing>
                </div>
              )}
            </div>

            {items.map((item, i) => {
              // 흡수 중·펼치기 직전에는 모두 가운데에, 작은 원 크기로
              const small = connectGeo!.small
              const n = phase === 'absorb' || phase === 'spread' ? { x: centerOf.cx, y: centerOf.cy, d: small } : geo.nodes[i]
              const absent = item.status === 'Absent'
              const style = {
                left: n.x - n.d / 2,
                top: n.y - n.d / 2,
                width: n.d,
                '--d': `${n.d}px`,
                '--i': i,
              } as CSSProperties

              if (mode === 'edit')
                return (
                  <EditNode
                    key={item.id}
                    item={item}
                    style={style}
                    // 목록(약 300px)은 오른쪽으로 열되, 창 오른쪽에 자리가 없으면 왼쪽으로
                    left={box.left + n.x + n.d / 2 + LIST_ROOM > box.vw}
                    listOpen={listKind === item.id}
                    error={editError?.kind === item.id ? editError.message : null}
                    onToggleList={() => {
                      setEditError(null)
                      setListKind((k) => (k === item.id ? null : item.id))
                    }}
                    onCloseList={() => setListKind(null)}
                    onSelect={(device) => select(item.id, device)}
                  />
                )

              const canPick = selectable(item)
              const isPicked = selected?.id === item.id
              return (
                // 원 안에 버튼(다시 연결·없이 진행)이 들어가므로 원 자체는 div + role=button
                <div
                  key={item.id}
                  role="button"
                  className={styles.node}
                  data-status={item.status}
                  data-absent={absent}
                  data-selectable={canPick}
                  data-hub={item.id === HUB}
                  data-selected={isPicked}
                  style={style}
                  onClick={() => togglePick(item)}
                  onKeyDown={(e) => {
                    if (e.target !== e.currentTarget || (e.key !== 'Enter' && e.key !== ' ')) return
                    e.preventDefault()
                    togglePick(item)
                  }}
                  tabIndex={canPick ? 0 : -1}
                  aria-disabled={!canPick}
                  aria-pressed={canPick ? isPicked : undefined}
                  aria-label={absent ? `${item.title}: 등록되지 않음` : `${item.title} ${item.term ?? ''}: ${statusText(item)}`}
                >
                  {absent ? (
                    <>
                      <span className={styles.smallIcon}>
                        <DeviceIcon kind={item.id} />
                      </span>
                      {/* 가리키면 원 위에 장비 종류 이름 (2026-10-01 사용자 결정) */}
                      <span className={styles.nodeTip} aria-hidden="true">
                        {item.title}
                      </span>
                    </>
                  ) : (
                    <>
                      <span className={styles.kind}>{item.title}</span>
                      {item.term && <span className={styles.name}>{item.term}</span>}
                      {/* 상태 아이콘 자리는 항상 둔다 (글자가 움직이지 않게) */}
                      <span className={styles.mark} aria-hidden="true">
                        {item.status === 'Pass' && <Check strokeWidth={2.5} />}
                        {(item.status === 'Fail' || item.status === 'Warn') && <AlertTriangle strokeWidth={2} />}
                      </span>
                      {/* 연결 안 된 원을 가리키면(hover·포커스): 이름·경고 대신 가운데에 다시 연결 아이콘 (준필수는 그 아래 "없이 진행") */}
                      {canPick && (
                        <span className={styles.hoverBody} data-without={item.status === 'Fail' && item.severity === 'Recommended'}>
                          <button
                            type="button"
                            className={styles.retryIcon}
                            onClick={(e) => {
                              e.stopPropagation()
                              void retry(item)
                            }}
                            aria-label={`${item.title} 다시 연결`}
                          >
                            <RotateCw strokeWidth={2} />
                          </button>
                          {item.status === 'Fail' && item.severity === 'Recommended' && (
                            <button
                              type="button"
                              className={styles.textAction}
                              onClick={(e) => {
                                e.stopPropagation()
                                void goWithout(item)
                              }}
                            >
                              없이 진행
                            </button>
                          )}
                        </span>
                      )}
                    </>
                  )}
                </div>
              )
            })}
          </>
        )}
      </div>

      {/* 아래: 지금 관측지 문장 + 진행 막대. 높이는 고정이라 무엇이 보이든 위의 원들은 움직이지 않는다.
          막대가 도는 동안에만 문장·막대가 보이고, "변경"을 가리키면 막대가 느려진다 */}
      <div className={styles.bottom}>
        <div className={styles.siteLine} data-shown={counting && site !== undefined} data-none={!site?.current}>
          {site?.current ? (
            <>
              <span>
                현재 관측지는 <b className={styles.siteCoord}>{coordText(site.current)}</b>
                {site.name && ` (${site.name})`}입니다
              </span>
              <button type="button" className={styles.siteChange} onClick={() => onContinue(items, { changeSite: true })} disabled={!counting} {...slowProps}>
                변경
              </button>
            </>
          ) : (
            <span>현재 관측지가 없습니다</span>
          )}
        </div>
        {/* 4초 진행 막대: 모두 연결되면 나타나 차오르고, 다 차면 다음 단계로. 그 전·변경 모드에서는 자리만 둔다. 길이는 위 효과가 그린다 */}
        <div className={styles.progress} data-shown={counting} aria-hidden="true">
          <div ref={fillRef} className={styles.progressFill} />
        </div>
      </div>

      {mode === 'connect' && <TempFailButtons items={items} done={done} patch={patch} />}
    </main>
  )
}

/**
 * 변경 모드의 아이콘 원.
 * - 누르면(selected) 원 옆에 드라이버 목록이 바로 열리고, 한 번 더 누르면 닫히며 normal로 돌아간다
 * - 누른 원(목록이 열림)에만 이름 아래에 "제거"가 보인다 (등록된 선택·준필수 장비만). 가리키기만 해서는 안 보인다
 */
function EditNode({
  item,
  style,
  left,
  listOpen,
  error,
  onToggleList,
  onCloseList,
  onSelect,
}: {
  item: CheckItem
  style: CSSProperties
  /** 오른쪽에 자리가 없으면 목록을 왼쪽으로 연다 */
  left: boolean
  listOpen: boolean
  /** 바꾸기·제거에 실패했을 때 원 아래에 보여 줄 문장 (원래 장비는 그대로) */
  error: string | null
  onToggleList: () => void
  onCloseList: () => void
  onSelect: (device: { id: string; name: string } | null) => void
}) {
  const absent = item.status === 'Absent'
  const canRemove = !absent && item.severity !== 'Required'
  return (
    <div className={styles.editNode} data-absent={absent} data-selected={listOpen} style={style}>
      <button
        type="button"
        className={styles.editCircle}
        onClick={onToggleList}
        aria-expanded={listOpen}
        aria-label={`${item.title}: ${absent ? '등록되지 않음. 눌러서 등록' : `${item.term ?? ''}. 눌러서 변경`}`}
      >
        <DeviceIcon kind={item.id} />
      </button>
      <div className={styles.editLabel}>
        <b>{item.title}</b>
        {!absent && <small>{item.term}</small>}
      </div>
      {canRemove && (
        <div className={styles.actions}>
          <button type="button" className={styles.textAction} onClick={() => onSelect(null)}>
            제거
          </button>
        </div>
      )}
      {error && (
        <p className={styles.editError} role="alert">
          {error}
        </p>
      )}
      {listOpen &&
        (item.id === SCOPE ? (
          <ScopeList left={left} onPick={onSelect} onClose={onCloseList} />
        ) : (
          <DeviceList kind={item.id} left={left} current={item.term} onPick={onSelect} onClose={onCloseList} />
        ))}
    </div>
  )
}

/** 설치된 드라이버 목록 (N.I.N.A.가 이 PC에서 찾은 것). 원 옆에 작게 열린다 */
function DeviceList({
  kind,
  left,
  current,
  onPick,
  onClose,
}: {
  kind: string
  left: boolean
  current: string | null
  onPick: (device: { id: string; name: string }) => void
  onClose: () => void
}) {
  const [devices, setDevices] = useState<{ id: string; name: string }[] | null>(null)
  const [error, setError] = useState(false)
  const load = useCallback(() => {
    setDevices(null)
    setError(false)
    fetch(`/api/equipment/devices/${kind}`)
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then(setDevices, () => setError(true))
  }, [kind])
  useEffect(load, [load])

  // Esc로 닫기
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className={styles.deviceList} data-left={left} role="listbox" aria-label="설치된 드라이버">
      {devices === null && !error && <p className={styles.listNote}>드라이버 목록을 읽는 중입니다</p>}
      {error && <p className={styles.listNote}>N.I.N.A.에서 목록을 받지 못했습니다</p>}
      {devices?.map((d) => (
        <button key={d.id} type="button" role="option" aria-selected={d.name === current} onClick={() => onPick(d)}>
          {d.name}
        </button>
      ))}
      <p className={styles.listNote}>
        목록에 없나요? 제조사 드라이버를 설치한 뒤{' '}
        <button type="button" className={styles.textAction} onClick={load}>
          새로고침
        </button>
      </p>
      {kind === 'camera' && <CameraPower />}
    </div>
  )
}

/** "Regulated 0-13.2V adjustable DC2: DC2" → "DC2" */
const shortOutlet = (name: string) => (name.includes(':') ? name.slice(name.lastIndexOf(':') + 1).trim() : name)

/**
 * 카메라 전원 (docs/PRECHECK_DESIGN.md J05, 2026-10-09): 배터리 또는 전원 허브 출력. 출력을 고르면 장비 연결 때 카메라가 안 보이면 아이라가 그 출력을 켠다.
 * 아이라는 출력 전압을 읽거나 바꾸지 못하므로, 고를 때 Empire에서 전압을 맞췄는지 한 번 확인받는다 (실기: 7~8.4V 어댑터를 12V 출력에 꽂아 켜지지 않음)
 */
function CameraPower() {
  const [state, setState] = useState<{ outlet: string | null; outlets: string[] } | null>(null)
  const [asking, setAsking] = useState<string | null>(null)
  useEffect(() => {
    fetch('/api/equipment/camera-power')
      .then((r) => (r.ok ? r.json() : null))
      .then(setState, () => setState(null))
  }, [])
  const save = (outlet: string | null) =>
    void fetch('/api/equipment/camera-power', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ outlet }) })
      .then((r) => (r.ok ? r.json() : null))
      .then((b: { outlet: string | null } | null) => b && setState((s) => (s ? { ...s, outlet: b.outlet } : s)))
  if (!state) return null
  return (
    <div className={styles.powerPick}>
      <p className={styles.listNote}>카메라 전원</p>
      <div className={styles.powerChoices} role="radiogroup" aria-label="카메라 전원">
        <button type="button" role="radio" aria-checked={state.outlet === null} onClick={() => save(null)}>
          배터리
        </button>
        {state.outlets.map((o) => (
          <button key={o} type="button" role="radio" aria-checked={state.outlet === o} onClick={() => state.outlet !== o && setAsking(o)}>
            허브 {shortOutlet(o)}
          </button>
        ))}
      </div>
      {state.outlets.length === 0 && <p className={styles.listNote}>전원 허브가 연결되면 허브 출력도 고를 수 있습니다</p>}
      <ConfirmDialog
        open={asking !== null}
        message={`Wanderer Empire에서 ${asking ? shortOutlet(asking) : ''} 출력 전압을 카메라 전원 어댑터에 맞게(어댑터에 적힌 입력 범위 안, 예: 8V) 맞췄나요? 전압이 맞지 않으면 카메라나 어댑터가 상할 수 있습니다. 아이라는 전압을 바꾸지 않고 켜고 끄기만 합니다.`}
        confirmLabel="맞췄어요"
        onConfirm={() => {
          save(asking)
          setAsking(null)
        }}
        onCancel={() => setAsking(null)}
      />
    </div>
  )
}

function statusText(item: CheckItem) {
  switch (item.status) {
    case 'Pass':
      return '연결되어 있습니다'
    case 'Fail':
      return item.message || '연결되어 있지 않습니다'
    case 'Running':
      return '연결하는 중입니다'
    case 'Skipped':
      return '전원 허브를 먼저 연결해 주세요'
    case 'Warn':
      return item.message
    default:
      return '연결 대기 중입니다'
  }
}

/** 누를 수 있는 원: 연결되지 않은 장비(실패·경고) */
function selectable(item: CheckItem) {
  return item.status === 'Fail' || item.status === 'Warn'
}

function reduceMotion() {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
}

interface Geo {
  cx: number
  cy: number
  /** 가운데 원 지름 */
  cd: number
  /** 작은 원(등록 안 된 장비) 지름 */
  small: number
  nodes: { x: number; y: number; d: number }[]
}

/**
 * 연결 모드: 원들의 자리와 크기 (px). 정다각형처럼 보이지 않게:
 * - 옆으로 약간 넓은 타원 궤도 위에, 각도·거리·크기를 장비마다 조금씩 흔든다 (흔든 값은 장비 id로 정해짐)
 * - **어느 자리에 어느 장비를 둘지는 앱을 켤 때마다 무작위**, 단 원 넓이가 위·아래·좌·우로 고르게
 *   퍼지는 순서를 고른다 (여러 번 섞어 보고 넓이의 무게중심이 가운데에 가장 가까운 것)
 *   한 번 정한 순서는 앱을 다시 켤 때까지 그대로 (sessionOrder) — 장비를 바꾸거나 화면에 다시 들어와도
 *   원들이 제자리에 있다 (레이아웃 안정성)
 * - 등록 안 된 장비는 가장 작은 장비 원의 30% 크기
 * - 원끼리·가운데 원과 겹치면 서로 밀어내고, 그래도 안 되면 전체를 조금씩 줄여 맞춘다
 */
/** 앱을 켠 동안 유지하는 장비 자리 순서 (모듈 변수라 화면을 다시 열어도 남고, 앱을 다시 켜면 새로 정한다) */
let sessionOrder: string[] | null = null
const sessionSeed = Math.floor(Math.random() * 2 ** 31)

function connectLayout(items: CheckItem[], w: number, h: number): Geo | null {
  if (w < 50 || h < 50 || items.length === 0) return null
  const cx = w / 2
  const cy = h / 2
  const base = Math.min(h, w * 0.75)
  const isSmallById = new Map(items.map((i) => [i.id, i.status === 'Absent']))
  const areaOf = (id: string) => {
    const d = isSmallById.get(id) ? SMALL * base * NODE * (1 - SIZE_SPREAD) : base * NODE * (1 + (seeded(id, 1) * 2 - 1) * SIZE_SPREAD)
    return d * d
  }
  // 처음 한 번만 정하고, 그 뒤로는 같은 순서 (장비 종류는 늘 같은 8가지라 목록이 같으면 그대로 쓴다)
  const idList = items.map((i) => i.id)
  const sameSet = sessionOrder !== null && sessionOrder.length === idList.length && idList.every((id) => sessionOrder!.includes(id))
  if (!sameSet) sessionOrder = balancedOrder(idList, areaOf, sessionSeed)
  const ids = sessionOrder!.slice(0)
  const isSmall = ids.map((id) => isSmallById.get(id) ?? false)

  let last: Geo | null = null
  for (let scale = 1; scale > 0.4; scale *= 0.94) {
    const cd = base * CENTER * scale
    // 글자가 든 원은 이름이 넘치지 않을 만큼은 크게 (최소 84px)
    const big = ids.map((id) => Math.max(84, base * NODE * scale * (1 + (seeded(id, 1) * 2 - 1) * SIZE_SPREAD)))
    const smallest = Math.min(...big.filter((_, i) => !isSmall[i]), base * NODE * scale)
    // 원끼리 최소 간격 = 실제로 그려지는 가장 작은 장비 원(글자가 든 원)의 반지름
    const bigOnly = big.filter((_, i) => !isSmall[i])
    const gap = (bigOnly.length > 0 ? Math.min(...bigOnly) : base * NODE * scale) / 2
    const small = Math.max(26, smallest * SMALL)
    const sizes = big.map((d, i) => (isSmall[i] ? small : d))
    const maxR = Math.max(...sizes) / 2
    const ry = h / 2 - maxR - 8
    const rx = Math.min(w / 2 - maxR - 8, ry * 1.5)
    if (ry <= cd / 2) continue

    const nodes = ids.map((id, i) => {
      const angle = ((-90 + (i * 360) / ids.length + (seeded(id, 2) * 2 - 1) * ANGLE_JITTER) * Math.PI) / 180
      // 작은 원은 가운데에 조금 더 가깝게 (큰 원 사이 빈자리에)
      const f = (1 + (seeded(id, 3) * 2 - 1) * RADIUS_JITTER) * (isSmall[i] ? 0.8 : 1)
      return { x: cx + Math.cos(angle) * rx * f, y: cy + Math.sin(angle) * ry * f, d: sizes[i] }
    })

    // 자리는 섞은 순서로 정했으니, 돌려줄 때는 원래 장비 순서(items)로 되돌린다
    const byId = new Map(ids.map((id, k) => [id, nodes[k]]))
    last = { cx, cy, cd, small, nodes: items.map((i) => byId.get(i.id)!) }
    if (relax(nodes, cx, cy, cd, w, h, gap)) return last
  }
  // 아주 좁은 화면: 완전히 풀리지 않아도 마지막 배치를 쓴다 (아무것도 안 그리는 것보다 낫다)
  return last
}

/**
 * 원 넓이가 고르게 퍼지는 자리 순서: seed로 여러 번(200) 섞어 보고,
 * 둘레에 같은 간격으로 놓았을 때 넓이의 무게중심(위·아래·좌·우 치우침)이 가장 작은 순서와 시작 각도를 고른다.
 */
function balancedOrder(ids: string[], areaOf: (id: string) => number, seed: number): string[] {
  const rand = mulberry32(seed)
  const n = ids.length
  let best = ids
  let bestScore = Infinity
  for (let t = 0; t < 200; t++) {
    const order = [...ids]
    for (let i = n - 1; i > 0; i--) {
      const j = Math.floor(rand() * (i + 1))
      ;[order[i], order[j]] = [order[j], order[i]]
    }
    let sx = 0
    let sy = 0
    order.forEach((id, k) => {
      const a = -Math.PI / 2 + (k * 2 * Math.PI) / n
      sx += Math.cos(a) * areaOf(id)
      sy += Math.sin(a) * areaOf(id)
    })
    const score = Math.hypot(sx, sy)
    if (score < bestScore) {
      bestScore = score
      best = order
    }
  }
  return best
}

/** seed로 정해지는 난수 (0~1). 같은 seed면 같은 순서 */
function mulberry32(seed: number) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

/**
 * 변경 모드: 장비 8종을 등급별 세 겹 궤도에. 필수가 가장 가깝게, 준필수 중간, 선택 바깥.
 * 원 크기는 모두 같고, 겹마다 시작 각도를 달리해 서로 줄 서지 않게 한다.
 */
/**
 * 변경 모드 원의 가로 자리 (2026-09-30 사용자 지정): 창 전체 너비를 0~10으로 나눈 값, 원 중심 기준.
 * 단 기준 너비는 그래프 너비의 1.25배까지 (2026-10-01, 3440px 울트라와이드에서 원들이 양 끝에 한 줄로 몰리던 문제).
 * 오른쪽 넷을 정하고, 왼쪽은 같은 겹에서 마주 보는 원을 좌우 대칭(10 - x)으로 둔다.
 * 세로 자리는 등급별 궤도 계산을 그대로 쓴다.
 */
const RIGHT_X = { camera: 6.7, guider: 7, switch: 8, filterwheel: 8.5 }
const EDIT_X: Record<string, number> = {
  ...RIGHT_X,
  mount: 10 - RIGHT_X.camera, // 필수: 가운데 원 좌우
  focuser: 10 - RIGHT_X.guider, // 준필수: 대각선
  flatdevice: 10 - RIGHT_X.switch, // 선택 위쪽
  rotator: 10 - RIGHT_X.filterwheel, // 선택 아래쪽
}

function editLayout(items: CheckItem[], w: number, h: number, screenX?: (frac: number) => number): Geo | null {
  if (w < 50 || h < 50 || items.length === 0) return null
  const cx = w / 2
  const cy = h / 2
  const base = Math.min(h, w * 0.75)
  const cd = base * CENTER * EDIT_CENTER_SCALE // 변경 모드에서는 가운데 원이 조금 작아진다 (CSS와 같게)
  const d = Math.max(52, base * EDIT_NODE)
  const tierOf = (i: CheckItem) => (i.severity === 'Required' ? 0 : i.severity === 'Recommended' ? 1 : 2)
  // 겹마다 가운데에서의 거리. 세로는 화면 높이에 막히므로 바깥 겹일수록 옆으로 넓은 타원
  const r = [0, 1, 2].map((t) => cd / 2 + d / 2 + 18 + t * (d + 22))
  const maxY = h / 2 - d / 2 - 40 // 이름 글자 자리
  const ry = r.map((v) => Math.min(v, maxY))
  const sx = Math.min(1, (w / 2 - d / 2 - 12) / (r[2] * 1.3))
  const rx = r.map((v, t) => v * (t === 0 ? 1 : 1.3) * sx)
  // 겹마다 자리(도): 필수는 가운데 원 좌우(이름 글자가 가운데 원에 닿지 않게), 준필수는 대각선, 선택은 바깥 좌우
  const angles = [
    [180, 0],
    [-120, 60],
    [-30, 30, 150, 210],
  ]
  // 망원경은 필수지만 사용자가 정한 적도의·카메라 자리를 바꾸지 않게 따로: 가운데 원 바로 위
  const byTier = [0, 1, 2].map((t) => items.filter((i) => i.id !== SCOPE && tierOf(i) === t))
  const nodes = items.map((item) => {
    if (item.id === SCOPE) return { x: cx, y: cy - ry[1], d }
    const t = tierOf(item)
    const k = byTier[t].indexOf(item)
    const deg = byTier[t].length === angles[t].length ? angles[t][k] : -90 + (k * 360) / byTier[t].length
    const a = (deg * Math.PI) / 180
    const fixedX = EDIT_X[item.id] !== undefined && screenX ? screenX(EDIT_X[item.id] / 10) : null
    // 그래프 영역 밖으로는 나가지 않게
    const x = fixedX === null ? cx + Math.cos(a) * rx[t] : Math.min(w - d / 2 - 4, Math.max(d / 2 + 4, fixedX))
    return { x, y: cy + Math.sin(a) * ry[t], d }
  })
  return { cx, cy, cd, small: d * 0.5, nodes }
}

/** 겹침 풀기: 몇 번 반복해 서로 밀어낸다 (정해진 순서라 결과는 항상 같다). 풀리면 true */
function relax(nodes: { x: number; y: number; d: number }[], cx: number, cy: number, cd: number, w: number, h: number, gap: number) {
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
        const need = p.d / 2 + q.d / 2 + gap
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
    if (!moved) return true
  }
  return false
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
  // 모의 장비일 때만 (실장비에서는 이 버튼이 실제 다시 연결이 되어 헷갈림 — 2026-10-08 시뮬레이터 전체 시험에서 발견)
  const [simulated, setSimulated] = useState(false)
  useEffect(() => {
    let alive = true
    fetch('/api/equipment/simulated')
      .then((r) => (r.ok ? (r.json() as Promise<{ simulated: boolean }>) : null))
      .then((b) => alive && setSimulated(!!b?.simulated))
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])
  const fail = async (id: string) => {
    const res = await fetch(`/api/equipment/connect/${id}?simulateFail=true`)
    if (!res.ok) return
    const result = (await res.json()) as CheckItem
    patch((prev) =>
      prev.map((i) =>
        i.id === id
          ? result
          : id === HUB
            ? { ...i, status: i.status === 'Absent' ? i.status : ('Skipped' as const), message: WAITING_FOR_HUB, diagnosis: null }
            : i,
      ),
    )
  }

  const connected = items.filter((i) => i.status === 'Pass')
  if (!simulated || !done || connected.length === 0) return null
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
