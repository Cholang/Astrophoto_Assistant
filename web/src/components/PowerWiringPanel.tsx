import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { DEFAULT_WIRING, type PowerSource, type PowerWiring } from '../profiles'
import styles from './PowerWiringPanel.module.css'

/**
 * 전원 배선 패널 (DESIGN.md 3장 "프로필: 전원 배선", 시안 mockups/aira-power-wiring.html — 2026-10-10 사용자 확정).
 * 장비 종류 사이의 전원 흐름만: DC 전원 → 적도의·파워박스(서로 앞뒤) → 나머지 장비. 선이 없으면 자체 전원(배터리 등).
 * 상자 어디서든 다른 상자로 끌거나 차례로 누르면 이어진다(방향 무관). 배선도의 크기·자리는 고정
 */
type Id = 'dc' | 'mount' | 'switch' | 'camera' | 'focuser' | 'filterwheel' | 'rotator' | 'guider' | 'heater' | 'flatdevice'

interface Box { x: number; y: number; w: number; h: number; name: string; tier: 0 | 1 | 2 }

// viewBox 1000×520 안의 고정 자리. 오른쪽 장비는 위에서부터 광학 경로 순서, 플랫패널이 맨 아래
const BOXES: Record<Id, Box> = {
  dc: { x: 40, y: 231, w: 150, h: 58, name: 'DC 전원', tier: 0 },
  mount: { x: 350, y: 140, w: 180, h: 66, name: '적도의', tier: 1 },
  switch: { x: 350, y: 314, w: 180, h: 66, name: '파워박스', tier: 1 },
  camera: { x: 760, y: 18, w: 190, h: 50, name: '카메라', tier: 2 },
  focuser: { x: 760, y: 85, w: 190, h: 50, name: '포커서', tier: 2 },
  filterwheel: { x: 760, y: 152, w: 190, h: 50, name: '필터휠', tier: 2 },
  rotator: { x: 760, y: 219, w: 190, h: 50, name: '회전장치', tier: 2 },
  guider: { x: 760, y: 286, w: 190, h: 50, name: '가이드 카메라', tier: 2 },
  heater: { x: 760, y: 353, w: 190, h: 50, name: '이슬 방지 열선', tier: 2 },
  flatdevice: { x: 760, y: 420, w: 190, h: 50, name: '플랫패널', tier: 2 },
}
const IDS = Object.keys(BOXES) as Id[]
const RECEIVERS = IDS.filter((id) => id !== 'dc')

const inPos = (id: Id) => ({ x: BOXES[id].x, y: BOXES[id].y + BOXES[id].h / 2 })
const outPos = (id: Id) => ({ x: BOXES[id].x + BOXES[id].w, y: BOXES[id].y + BOXES[id].h / 2 })

/** 앞으로 가는 선은 옆으로 휘고, 같은 열의 다른 상자로 가는 선(적도의 ↔ 파워박스)은 오른쪽으로 돌아 왼쪽으로 들어간다 */
function curve(a: { x: number; y: number }, b: { x: number; y: number }) {
  if (b.x <= a.x) {
    const my = (a.y + b.y) / 2
    const mx = (a.x + b.x) / 2
    return `M${a.x},${a.y} C${a.x + 70},${a.y} ${a.x + 70},${my} ${mx},${my} S${b.x - 70},${b.y} ${b.x},${b.y}`
  }
  const m = Math.max(40, (b.x - a.x) / 2)
  return `M${a.x},${a.y} C${a.x + m},${a.y} ${b.x - m},${b.y} ${b.x},${b.y}`
}

/** 두 상자를 이으면 누가 주는지 (끈 방향 무관). 적도의 ↔ 파워박스는 끈 쪽이 주는 쪽. 이을 수 없으면 이유 */
function pair(a: Id, b: Id): { from: Id; to: Id } | string | null {
  if (a === b) return null
  const ta = BOXES[a].tier
  const tb = BOXES[b].tier
  if (ta === tb && ta === 2) return '장비끼리는 전원을 주고받지 않습니다'
  if (Math.abs(ta - tb) === 2) return 'DC 전원은 적도의나 파워박스로 이어 주세요'
  if (ta === tb) return { from: a, to: b }
  return ta < tb ? { from: a, to: b } : { from: b, to: a }
}

export default function PowerWiringPanel({
  value,
  onChange,
  onClose,
}: {
  value: PowerWiring
  onChange: (next: PowerWiring) => void
  onClose: () => void
}) {
  const svg = useRef<SVGSVGElement>(null)
  const [sel, setSel] = useState<Id | null>(null)
  const [drag, setDrag] = useState<{ id: Id; start: { x: number; y: number }; at: { x: number; y: number }; moved: boolean } | null>(null)
  const [hover, setHover] = useState<Id | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const source = (id: Id): PowerSource => (id in value ? value[id] : (DEFAULT_WIRING[id] ?? null))

  useEffect(() => {
    if (!notice) return
    const t = window.setTimeout(() => setNotice(null), 2400)
    return () => clearTimeout(t)
  }, [notice])

  const connect = (a: Id, b: Id) => {
    const p = pair(a, b)
    if (!p) return
    if (typeof p === 'string') return setNotice(p)
    const next: PowerWiring = { ...DEFAULT_WIRING, ...value }
    // 적도의 ↔ 파워박스 순서를 뒤집으면 앞이 되는 쪽이 DC 전원을 받는다 (서로에게서 받지 않게)
    if (next[p.from] === p.to) next[p.from] = 'dc'
    next[p.to] = p.from as PowerSource
    onChange(next)
  }
  const cut = (id: Id) => onChange({ ...DEFAULT_WIRING, ...value, [id]: null })

  // 누르기만 하면 고름 → 다른 상자를 누르면 이음
  const pick = (id: Id) => {
    if (sel && sel !== id) {
      setSel(null)
      connect(sel, id)
      return
    }
    setSel(sel === id ? null : id)
  }

  const toSvg = (x: number, y: number) => {
    const s = svg.current!
    const pt = s.createSVGPoint()
    pt.x = x
    pt.y = y
    return pt.matrixTransform(s.getScreenCTM()!.inverse())
  }
  const boxAt = (x: number, y: number) => (document.elementFromPoint(x, y)?.closest('[data-box]')?.getAttribute('data-box') as Id | null) ?? null

  const startDrag = (e: ReactPointerEvent, id: Id) => {
    e.preventDefault()
    const start = toSvg(e.clientX, e.clientY)
    let state = { id, start, at: start, moved: false }
    setDrag(state)
    const move = (ev: PointerEvent) => {
      const p = toSvg(ev.clientX, ev.clientY)
      state = { ...state, at: p, moved: state.moved || Math.hypot(p.x - start.x, p.y - start.y) > 6 }
      setDrag(state)
      const t = boxAt(ev.clientX, ev.clientY)
      setHover(t && t !== id ? t : null)
    }
    const up = (ev: PointerEvent) => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', up)
      setDrag(null)
      setHover(null)
      if (!state.moved) return pick(id)
      setSel(null)
      const t = boxAt(ev.clientX, ev.clientY)
      if (t && t !== id) connect(id, t)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', up)
  }

  const busy = sel ?? drag?.id ?? null

  return (
    <section className={styles.panel} aria-label="전원 배선">
      <div className={styles.head}>
        <div>
          <h2>장비에 전원이 어떻게 들어가나요?</h2>
          <p>설정해 두면 아이라가 이 배선을 믿고 안내합니다. 실제와 다른 것을 발견하면 아이라가 고쳐 둡니다.</p>
        </div>
        <button type="button" className={styles.close} onClick={onClose}>
          닫기
        </button>
      </div>

      <div className={styles.board}>
        <svg ref={svg} viewBox="0 0 1000 520" preserveAspectRatio="xMidYMid meet" onKeyDown={(e) => e.key === 'Escape' && setSel(null)}>
          <g>
            {RECEIVERS.map((id) => {
              const src = source(id) as Id | null
              if (!src) return null
              const a = outPos(src)
              const b = inPos(id)
              const d = curve(a, b)
              return (
                <g key={id}>
                  <path d={d} className={styles.hit} onClick={() => cut(id)}>
                    <title>누르면 끊깁니다 (그 장비는 자체 전원)</title>
                  </path>
                  <path d={d} className={styles.wire} />
                  <circle cx={a.x} cy={a.y} r={4} className={styles.end} />
                  <circle cx={b.x} cy={b.y} r={4} className={styles.end} />
                </g>
              )
            })}
            {drag?.moved && <path d={`M${drag.start.x},${drag.start.y} L${drag.at.x},${drag.at.y}`} className={`${styles.wire} ${styles.temp}`} />}
          </g>
          <g>
            {IDS.map((id) => {
              const b = BOXES[id]
              const bad = busy && busy !== id && typeof pair(busy, id) === 'string'
              return (
                <g
                  key={id}
                  data-box={id}
                  className={styles.box}
                  data-source={id === 'dc'}
                  data-sel={sel === id}
                  data-target={hover === id}
                  data-dim={!!bad}
                  tabIndex={0}
                  role="button"
                  aria-label={`${b.name} 잇기`}
                  onPointerDown={(e) => startDrag(e, id)}
                  onKeyDown={(e) => e.key === 'Enter' && pick(id)}
                >
                  <rect x={b.x} y={b.y} width={b.w} height={b.h} rx={12} />
                  <text x={b.x + 20} y={b.y + b.h / 2 + 6}>
                    {b.name}
                  </text>
                  {id !== 'dc' && !source(id) && (
                    <text x={b.x + b.w - 16} y={b.y + b.h / 2 + 5} textAnchor="end" className={styles.self}>
                      자체 전원
                    </text>
                  )}
                </g>
              )
            })}
          </g>
        </svg>
        <p className={styles.hint}>상자를 다른 상자로 끌거나 차례로 누르면 이어집니다 · 선을 누르면 끊깁니다 · 선이 없으면 자체 전원</p>
        <p className={styles.notice} data-on={!!notice} role="status">
          {notice}
        </p>
      </div>

      <div className={styles.foot}>
        <button type="button" className={styles.reset} onClick={() => onChange({ ...DEFAULT_WIRING })}>
          처음 배선으로
        </button>
      </div>
    </section>
  )
}

/** 버튼의 한 줄 요약 */
export function wiringSummary(w: PowerWiring): string {
  const s = { ...DEFAULT_WIRING, ...w }
  const first = s.switch === 'mount' ? '적도의 먼저' : s.mount === 'switch' ? '파워박스 먼저' : '적도의·파워박스 따로'
  const camera = s.camera === 'switch' ? '파워박스' : s.camera === 'mount' ? '적도의' : '자체 전원'
  return `${first} · 카메라 ${camera}`
}
