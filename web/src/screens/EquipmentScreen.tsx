import { AlertTriangle, Check, RotateCw } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import Button from '../components/Button'
import ConfirmDialog from '../components/ConfirmDialog'
import DeviceIcon from '../components/DeviceIcon'
import JarvisRing from '../components/JarvisRing'
import JwstIcon from '../components/JwstIcon'
import ScopeList from '../components/ScopeList'
import { useCheckStream, type CheckItem } from '../checks'
import { scaleOf } from '../frame'
import { coordText, type CurrentSite } from '../sites'
import engineStyles from './EngineStartScreen.module.css'
import { PLANET_OF, solarLayout, type Planet, type SolarGeo } from './equipmentOrbits'
import styles from './EquipmentScreen.module.css'

/** 전원 허브: 다른 장비에 전원을 주므로, 허브가 실패하면 나머지는 보류하고 허브부터 해결한다 (서버와 같은 규칙) */
const HUB = 'switch'
const WAITING_FOR_HUB = '전원 허브가 연결되면 확인합니다'

// 자동 진행 (DESIGN.md 3장 "장비 연결"): 4초 동안 항성 테두리 호가 차오른다, "장비 변경"은 3.5초까지만 받는다
const ADVANCE_MS = 4000
const CHANGE_UNTIL_MS = 3500
const SLOW_RATE = 0.3 // 변경 버튼을 가리키는 동안 시간이 흐르는 배속
const LIST_ROOM = 310 // 드라이버 목록 폭 280 + 간격 (CSS와 같게)
/** 망원경 (서버 EquipmentConnector.Scope): 행성이 아니라 망원경 아이콘, 드라이버 목록 대신 망원경 목록을 연다 */
const SCOPE = 'scope'

type Mode = 'connect' | 'edit'

/**
 * 장비 연결 — 태양계 (2026-10-11 사용자 결정, 시안 mockups/aira-equipment-orbits-top.html).
 * 1단계의 원형 표시가 왼쪽 위 구석으로 옮겨 가며 커져 항성(1/4만 보임)이 되고, 장비는 1:1로 정해진 행성으로 궤도 위에 놓인다.
 * 연결된 장비는 자기 궤도가 희미하게 그려진다(연결선 대신). 등록되지 않은 장비는 같은 자리의 흐린 점선 행성(아이콘만).
 * 망원경은 행성이 아니라 제임스 웹 망원경 아이콘 — 연결과 관계없이 늘 눌러 바꿀 수 있다.
 * 모두 되면 항성 테두리 호가 4초 동안 차오른 뒤 다음 단계로. 그 사이 항성 안 "장비 변경"을 누르면 변경 모드 — 행성은 제자리에서 눌러 바꾼다.
 * [임시] 지금은 서버 설정 Equipment:Simulate로 연결을 흉내 낼 수 있다.
 */
/** 장비 연결 화면이 끝나고 넘어갈 때: changeSite면 관측지 고르기로 ("변경"을 누름) */
export type EquipmentDone = (items: CheckItem[], opts: { changeSite: boolean }) => void

/** 항성 안에 보여 줄 지금 관측지 (App이 N.I.N.A.에서 읽어 둔 값). undefined면 아직 모름 */
export interface SiteLine {
  current: CurrentSite | null
  name: string | null
}

export default function EquipmentScreen({ onContinue, site }: { onContinue: EquipmentDone; site?: SiteLine }) {
  const [plan, setPlan] = useState<CheckItem[] | null>(null)
  // 장비 목록을 받는 동안에는 1단계와 똑같은 자리·크기의 원을 그대로 보여 준다 (화면이 바뀌어도 원이 끊기지 않게).
  // 목록이 오면 그 원의 자리에서 항성이 옮겨 가도록 자리를 넘긴다.
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
  /** 1단계 원이 있던 자리 (화면 좌표). 항성이 여기서 옮겨 간다 */
  from: DOMRect | null
  site?: SiteLine
}) {
  const { items, done, allPass, failed, restart, recheckOne, patch } = useCheckStream('/api/equipment/connect', plan)
  const [picked, setPicked] = useState<string | null>(null)
  const sunRef = useRef<HTMLDivElement>(null)
  const graphRef = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ w: 0, h: 0 })
  const [moveFrom, setMoveFrom] = useState<{ scale: number; dx: number; dy: number } | null>(null)

  const [mode, setMode] = useState<Mode>('connect')
  const changed = useRef(new Set<string>())
  // 드라이버·망원경 목록을 연 장비 (selected). 망원경은 연결 모드에서도 열 수 있다
  const [listKind, setListKind] = useState<string | null>(null)

  // 화면 영역 크기를 재서 행성 자리를 계산한다 (창 크기가 바뀌면 다시)
  useLayoutEffect(() => {
    const el = graphRef.current
    if (!el) return
    const measure = () => {
      const next = { w: el.clientWidth, h: el.clientHeight }
      setBox((b) => (b.w === next.w && b.h === next.h ? b : next))
    }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const geo = useMemo(() => solarLayout(box.w, box.h), [box.w, box.h])

  // 1단계 원의 자리·크기에서 항성 자리·크기로 옮겨 가는 것처럼 시작한다 (처음 한 번만)
  useLayoutEffect(() => {
    if (moveFrom !== null || !geo) return
    const el = sunRef.current
    if (!el) return
    const now = el.getBoundingClientRect()
    if (!now.width) return
    // 화면에서 잰 값(확대·축소 적용)을 틀 안의 길이로 바꿔서 옮긴다 — translate는 틀 안에서 다시 확대·축소되므로
    const s = scaleOf(el)
    const frameH = el.closest<HTMLElement>('[data-frame]')?.clientHeight ?? window.innerHeight
    const engine = from ? from.width : Math.min(340, Math.max(240, frameH * 0.34)) * s
    setMoveFrom({
      scale: engine / now.width,
      dx: from ? (from.left + from.width / 2 - (now.left + now.width / 2)) / s : 0,
      dy: from ? (from.top + from.height / 2 - (now.top + now.height / 2)) / s : 0,
    })
  }, [geo, moveFrom, from])

  // ── 자동 진행: 모두 되면 4초 동안 항성 테두리 호가 차오른다. 그동안 "장비 변경"을 누르면 변경 모드로 ─────────
  // 목록(망원경 등)을 열어 두면 멈춘다
  const counting = mode === 'connect' && allPass && listKind === null
  const [late, setLate] = useState(false) // 3.5초가 지났으면 "장비 변경"을 받지 않는다
  // "장비 변경"·관측지 "변경"·망원경을 가리키는(또는 포커스) 동안은 시간이 0.3배로 흐른다 (2026-10-01 사용자 결정, 멈춤 대신)
  const slow = useRef(false)
  const arcRef = useRef<SVGPathElement>(null)
  const slowProps = {
    onPointerEnter: () => (slow.current = true),
    onPointerLeave: () => (slow.current = false),
    onFocus: () => (slow.current = true),
    onBlur: () => (slow.current = false),
  }
  const elapsed = useRef(0) // 흐른 시간(배속 반영). 장비 목록이 바뀌어 효과가 다시 돌아도 이어서
  useEffect(() => {
    const arc = arcRef.current
    if (!counting) {
      if (!allPass || mode !== 'connect') {
        elapsed.current = 0
        setLate(false)
      }
      if (arc) arc.style.strokeDashoffset = `${1 - Math.min(1, elapsed.current / ADVANCE_MS)}`
      return
    }
    let last = performance.now()
    let frame = 0
    const tick = (now: number) => {
      elapsed.current += (now - last) * (slow.current ? SLOW_RATE : 1)
      last = now
      if (arc) arc.style.strokeDashoffset = `${1 - Math.min(1, elapsed.current / ADVANCE_MS)}`
      if (elapsed.current >= CHANGE_UNTIL_MS) setLate(true)
      if (elapsed.current >= ADVANCE_MS) return onContinue(items, { changeSite: false })
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [counting, allPass, mode, items, onContinue])

  // ── 모드 전환: 행성은 제자리, 눌러서 바꾸는 모드로만 바뀐다 (2026-10-11 — 흡수·펼침 없음) ─────────
  const startEdit = () => {
    if (mode !== 'connect' || late) return
    slow.current = false // 버튼이 "변경 완료"로 바뀌어 가리킴이 끝났다는 알림을 못 받으므로
    changed.current.clear()
    setListKind(null)
    setPicked(null)
    setMode('edit')
  }

  // 변경 완료: 연결 모드로 돌아가 바뀐 장비만 연결한다 (허브가 바뀌면 처음부터)
  const finishEdit = () => {
    if (mode !== 'edit') return
    const kinds = [...changed.current]
    setListKind(null)
    setMode('connect')
    if (kinds.includes(HUB)) return restart()
    for (const kind of kinds) {
      const item = items.find((i) => i.id === kind)
      if (item && item.status !== 'Absent') void recheckOne(kind, `/api/equipment/connect/${kind}`)
    }
  }

  // 장비 고르기·제거: 서버에 저장하고 그 칸을 바꾼다
  // 실제 모드에서 N.I.N.A.가 바꾸기·제거에 실패하면 서버는 원래 장비를 그대로 두고 error를 돌려준다 → 그 행성 아래에 보여 준다
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

  // 목록 바깥 아무 데나 누르면 닫힌다 (2026-10-11 사용자 요청)
  useEffect(() => {
    if (listKind === null) return
    const onDown = (e: PointerEvent) => {
      if (!(e.target as Element | null)?.closest('[data-list-host="open"]')) setListKind(null)
    }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
  }, [listKind])

  // ── 항성 안 문장 ─────────
  // 어느 장비든 연결하는 중이면 '연결 중' (허브를 다시 연결하는 동안 '0개 있습니다'가 먼저 보이지 않게, 개수는 연결이 끝난 뒤에)
  const connecting = items.some((i) => i.status === 'Running')
  const state = allPass ? 'done' : connecting ? 'working' : done && failed ? 'failed' : 'working'
  const without = items.filter((i) => i.status === 'Warn')
  const missing = items.filter((i) => i.status === 'Fail' && i.severity !== 'Optional').length
  const hubFailed = items.some((i) => i.id === HUB && i.status === 'Fail')
  // 고른 행성: 연결되지 않은 장비(실패·경고)만 고를 수 있다. 연결되면 저절로 풀린다
  const selected = mode === 'connect' ? items.find((i) => i.id === picked && selectable(i)) : undefined
  const headline =
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
  // 개수: 등록된 장비(망원경 제외) 중 연결된 것
  const devices = items.filter((i) => i.id !== SCOPE && i.status !== 'Absent')
  const count = `${devices.filter((i) => i.status === 'Pass').length} / ${devices.length}`

  // 행성을 누르면 고르고, 한 번 더 누르면 풀린다
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

  const scope = items.find((i) => i.id === SCOPE)
  const planetItems = items.filter((i) => PLANET_OF[i.id] !== undefined)
  // 소행성대: 화성(포커서)이나 바깥 행성(목성부터)이 처음 연결되면 나타난다
  const beltOn = planetItems.some((i) => i.status === 'Pass' && (i.id === 'focuser' || PLANET_OF[i.id] >= 4))

  return (
    <main className={styles.stage}>
      <div ref={graphRef} className={styles.graph} data-mode={mode}>
        {geo && (
          <>
            {/* 궤도·고리·위성·소행성대: 연결된 장비만, 아주 희미하게 (연결선 대신) */}
            <svg className={styles.space} viewBox={`0 0 ${box.w} ${box.h}`} aria-hidden="true">
              <Corona geo={geo} />
              <g className={styles.belt} data-on={beltOn}>
                {geo.belt.map((b, k) => (
                  <circle key={k} cx={b.x} cy={b.y} r={b.r} opacity={b.o} />
                ))}
              </g>
              {planetItems.map((item) => {
                const p = geo.planets[PLANET_OF[item.id]]
                const on = item.status === 'Pass'
                return (
                  <g key={item.id} data-on={on}>
                    <path className={styles.orbit} d={orbitPath(geo, p)} pathLength={1} />
                    {item.status !== 'Absent' &&
                      p.bands.map((b, k) => (
                        <circle key={k} className={styles.ringBand} cx={p.x} cy={p.y} r={b.r} strokeWidth={b.width} strokeOpacity={b.opacity} />
                      ))}
                    <g className={styles.moons}>
                      {p.moons.map((m, k) => (
                        <circle key={k} cx={m.x} cy={m.y} r={m.r} />
                      ))}
                    </g>
                  </g>
                )
              })}
              {/* 모두 연결되면 4초 동안 차오르는 진행 호: 항성 테두리 중 보이는 부분 (길이는 위 효과가 정한다) */}
              <path ref={arcRef} className={styles.sunArc} d={sunArcPath(geo)} pathLength={1} data-shown={mode === 'connect' && allPass} />
            </svg>

            {/* 항성: 1단계 원이 옮겨 오며 커진다. 테두리 빛(연결 중·끝·실패)은 JarvisRing이 그린다 */}
            <div
              ref={sunRef}
              className={styles.sun}
              style={
                {
                  left: geo.cx,
                  top: geo.cy,
                  width: geo.sunD,
                  '--move-from': moveFrom?.scale ?? 1,
                  '--from-dx': `${moveFrom?.dx ?? 0}px`,
                  '--from-dy': `${moveFrom?.dy ?? 0}px`,
                } as CSSProperties
              }
              data-ready={moveFrom !== null}
            >
              <JarvisRing state={mode === 'edit' ? 'working' : state} className={`${styles.ring} ${styles.sunRing}`}>
                {null}
              </JarvisRing>
              {/* 1단계 끝 모습을 위에 겹쳐 두고, 옮겨 가는 동안 흐려진다 → 항성으로 크로스페이드 */}
              <div className={styles.fromRing} aria-hidden="true">
                <JarvisRing state="done" className={styles.ring}>
                  준비되었습니다
                </JarvisRing>
              </div>
            </div>

            {/* 항성 안(보이는 1/4)의 글자·버튼: 상태 문장 · 개수 · 장비 변경 · 관측지 */}
            <div className={styles.sunInfo} style={{ left: geo.cx + geo.sunD * 0.075, top: geo.cy + geo.sunD * 0.07 }} role="status" aria-live="polite">
              <p className={styles.headline}>{headline}</p>
              <p className={styles.count} data-shown={mode === 'connect'}>
                {count}
              </p>
              <div className={styles.modeRow}>
                {mode === 'connect' ? (
                  // 진행 호가 차오르는 동안 가리키면 시간이 느리게 흐른다. 버튼이 꺼져도 가리킨 상태를 놓치지 않게 감싼 칸에서 받는다
                  <span className={styles.slowZone} {...slowProps}>
                    <Button size="sm" onClick={startEdit} disabled={!done || late}>
                      장비 변경
                    </Button>
                  </span>
                ) : (
                  <Button size="sm" variant="primary" onClick={finishEdit}>
                    변경 완료
                  </Button>
                )}
              </div>
              {/* 지금 관측지: 자리는 항상 있고 진행 호가 차오를 때만 보인다 */}
              <div className={styles.siteLine} data-shown={counting && site !== undefined} data-none={!site?.current}>
                {site?.current ? (
                  <>
                    <span>
                      관측지 <b className={styles.siteCoord}>{coordText(site.current)}</b>
                      {site.name && ` (${site.name})`}
                    </span>
                    <button type="button" className={styles.textAction} onClick={() => onContinue(items, { changeSite: true })} disabled={!counting} {...slowProps}>
                      변경
                    </button>
                  </>
                ) : (
                  <span>현재 관측지가 없습니다</span>
                )}
              </div>
            </div>

            {planetItems.map((item, i) => {
              const p = geo.planets[PLANET_OF[item.id]]
              const style = { left: p.x - p.d / 2, top: p.y - p.d / 2, width: p.d, '--d': `${p.d}px`, '--i': i } as CSSProperties
              if (mode === 'edit')
                return (
                  <EditNode
                    key={item.id}
                    item={item}
                    style={style}
                    // 목록(약 300px)은 오른쪽으로 열되, 화면 오른쪽에 자리가 없으면 왼쪽으로
                    left={p.x + p.d / 2 + LIST_ROOM > box.w}
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
              return (
                <PlanetNode
                  key={item.id}
                  item={item}
                  planet={p}
                  style={style}
                  picked={selected?.id === item.id}
                  onPick={() => togglePick(item)}
                  onRetry={() => void retry(item)}
                  onWithout={() => void goWithout(item)}
                />
              )
            })}

            {/* 망원경: 제임스 웹 망원경 아이콘. 연결과 관계없이 늘 눌러서 바꾼다 (2026-10-11) */}
            {scope && (
              <div
                className={styles.scope}
                style={{ left: geo.scope.x, top: geo.scope.y }}
                data-status={scope.status}
                data-list-host={listKind === SCOPE ? 'open' : undefined}
              >
                <button
                  type="button"
                  className={styles.scopeButton}
                  aria-expanded={listKind === SCOPE}
                  aria-label={`망원경: ${scope.term ?? '고르지 않음'}. 눌러서 바꾸기`}
                  onClick={() => {
                    setEditError(null)
                    setListKind((k) => (k === SCOPE ? null : SCOPE))
                  }}
                  {...slowProps}
                >
                  <JwstIcon className={styles.jwst} />
                  <span className={styles.scopeName}>{scope.term ?? '망원경을 골라 주세요'}</span>
                </button>
                {editError?.kind === SCOPE && (
                  <p className={styles.editError} role="alert">
                    {editError.message}
                  </p>
                )}
                {listKind === SCOPE && <ScopeList left={geo.scope.x + LIST_ROOM > box.w} onPick={(s) => void select(SCOPE, s)} onClose={() => setListKind(null)} />}
              </div>
            )}
          </>
        )}
      </div>

      {mode === 'connect' && <TempFailButtons items={items} done={done} patch={patch} />}
    </main>
  )
}

/** 항성 테두리 바깥의 이글거리는 빛줄기 (크기·간격·밝기가 고르지 않은 흐린 타원 두 묶음이 서로 반대로 아주 천천히 돈다) */
function Corona({ geo }: { geo: SolarGeo }) {
  const R = geo.sunD / 2
  const rays = useMemo(() => {
    let s = 11
    const r = () => ((s = (s * 16807) % 2147483647) - 1) / 2147483646
    return [40, 30].map((n) =>
      Array.from({ length: n }, (_, k) => ({
        ang: (k / n) * 360 + r() * 9,
        len: R * (0.04 + r() * 0.09),
        wid: R * (0.015 + r() * 0.025),
        o: 0.07 + r() * 0.14,
      })),
    )
  }, [R])
  const origin = { transformOrigin: `${geo.cx}px ${geo.cy}px` }
  return (
    <g className={styles.corona}>
      <defs>
        <filter id="eq-corona-soft" x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation={9} />
        </filter>
      </defs>
      <circle cx={geo.cx} cy={geo.cy} r={R * 1.03} className={styles.halo} strokeWidth={R * 0.06} filter="url(#eq-corona-soft)" />
      {rays.map((set, gi) => (
        <g key={gi} className={styles.rays} data-dir={gi} style={origin} filter="url(#eq-corona-soft)">
          {set.map((ray, k) => (
            <ellipse
              key={k}
              cx={geo.cx + R + ray.len * 0.35}
              cy={geo.cy}
              rx={ray.len}
              ry={ray.wid}
              opacity={ray.o}
              transform={`rotate(${ray.ang} ${geo.cx} ${geo.cy})`}
            />
          ))}
        </g>
      ))}
    </g>
  )
}

/** 궤도: 행성 자리에서 시작해 한 바퀴 (그려질 때 행성에서 출발하는 것처럼) */
function orbitPath(geo: SolarGeo, p: Planet) {
  const th = (p.a * Math.PI) / 180
  const sx = geo.cx + p.r * Math.cos(th)
  const sy = geo.cy + p.r * Math.sin(th)
  const ox = geo.cx - p.r * Math.cos(th)
  const oy = geo.cy - p.r * Math.sin(th)
  return `M ${sx} ${sy} A ${p.r} ${p.r} 0 1 1 ${ox} ${oy} A ${p.r} ${p.r} 0 1 1 ${sx} ${sy}`
}

/** 진행 호: 항성 테두리 중 화면 안에 보이는 부분 (오른쪽 끝 → 아래 끝) */
function sunArcPath(geo: SolarGeo) {
  const r = geo.sunD / 2 + 1
  const a0 = Math.asin(Math.min(1, Math.max(0, -geo.cy / r)))
  const a1 = Math.PI / 2 - Math.asin(Math.min(1, Math.max(0, -geo.cx / r)))
  return `M ${geo.cx + r * Math.cos(a0)} ${geo.cy + r * Math.sin(a0)} A ${r} ${r} 0 0 1 ${geo.cx + r * Math.cos(a1)} ${geo.cy + r * Math.sin(a1)}`
}

/**
 * 연결 모드의 행성 하나. 등록된 장비: 장비 아이콘(오른쪽 아래에 연결 표시 V·!) + 제품 이름.
 * 등록 안 된 장비: 같은 자리·크기의 흐린 점선 행성, 아이콘만.
 * 연결 안 된 행성(실패·경고)을 가리키면 가운데에 다시 연결 아이콘 (준필수는 그 아래 "없이 진행")
 */
function PlanetNode({
  item,
  planet,
  style,
  picked,
  onPick,
  onRetry,
  onWithout,
}: {
  item: CheckItem
  planet: Planet
  style: CSSProperties
  picked: boolean
  onPick: () => void
  onRetry: () => void
  onWithout: () => void
}) {
  const absent = item.status === 'Absent'
  const canPick = selectable(item)
  return (
    // 행성 안에 버튼(다시 연결·없이 진행)이 들어가므로 행성 자체는 div + role=button
    <div
      role="button"
      className={styles.node}
      data-status={item.status}
      data-absent={absent}
      data-selectable={canPick}
      data-hub={item.id === HUB}
      data-selected={picked}
      style={style}
      title={item.title}
      onClick={onPick}
      onKeyDown={(e) => {
        if (e.target !== e.currentTarget || (e.key !== 'Enter' && e.key !== ' ')) return
        e.preventDefault()
        onPick()
      }}
      tabIndex={canPick ? 0 : -1}
      aria-disabled={!canPick}
      aria-pressed={canPick ? picked : undefined}
      aria-label={absent ? `${item.title}: 등록되지 않음` : `${item.title} ${item.term ?? ''}: ${statusText(item)}`}
    >
      {/* 목성: 내용 아래 희미한 대적점 */}
      {planet.spot && !absent && <span className={styles.spot} aria-hidden="true" />}
      <span className={styles.devWrap}>
        <DeviceIcon kind={item.id} className={styles.dev} />
        {/* 연결 표시는 행성 안 자리가 모자라 장비 아이콘 오른쪽 아래에 겹쳐서 (2026-10-11) */}
        {!absent && (
          <span className={styles.mark} data-shown={item.status === 'Pass' || item.status === 'Fail' || item.status === 'Warn'} aria-hidden="true">
            {item.status === 'Pass' && <Check strokeWidth={3} />}
            {(item.status === 'Fail' || item.status === 'Warn') && <AlertTriangle strokeWidth={2.5} />}
          </span>
        )}
      </span>
      {!absent && <span className={styles.name}>{item.term}</span>}
      {canPick && (
        <span className={styles.hoverBody} data-without={item.status === 'Fail' && item.severity === 'Recommended'}>
          <button
            type="button"
            className={styles.retryIcon}
            onClick={(e) => {
              e.stopPropagation()
              onRetry()
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
                onWithout()
              }}
            >
              없이 진행
            </button>
          )}
        </span>
      )}
    </div>
  )
}

/**
 * 변경 모드의 행성 (자리·크기는 연결 모드와 같다).
 * - 누르면(selected) 행성 옆에 드라이버 목록이 바로 열리고, 한 번 더 누르면 닫히며 normal로 돌아간다
 * - 누른 행성(목록이 열림)에만 이름 아래에 "제거"가 보인다 (등록된 선택·준필수 장비만). 가리키기만 해서는 안 보인다
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
  /** 바꾸기·제거에 실패했을 때 행성 아래에 보여 줄 문장 (원래 장비는 그대로) */
  error: string | null
  onToggleList: () => void
  onCloseList: () => void
  onSelect: (device: { id: string; name: string } | null) => void
}) {
  const absent = item.status === 'Absent'
  const canRemove = !absent && item.severity !== 'Required'
  return (
    <div className={styles.editNode} data-absent={absent} data-selected={listOpen} data-list-host={listOpen ? 'open' : undefined} style={style}>
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
      {listOpen && <DeviceList kind={item.id} left={left} current={item.term} onPick={onSelect} onClose={onCloseList} />}
    </div>
  )
}

/** 설치된 드라이버 목록 (N.I.N.A.가 이 PC에서 찾은 것). 행성 옆에 작게 열린다 */
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

/** 누를 수 있는 행성: 연결되지 않은 장비(실패·경고) */
function selectable(item: CheckItem) {
  return item.status === 'Fail' || item.status === 'Warn'
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
