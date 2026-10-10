/**
 * 장비 연결 화면의 태양계 배치 (2026-10-11 사용자 결정, 시안 mockups/aira-equipment-orbits-top.html).
 * 위에서 내려다본 평면: 항성(자비스 원)은 왼쪽 위 구석에 1/4만 보이고, 장비는 1:1로 정해진 행성의 동심원 궤도 위에 놓인다.
 * 자리는 장비 상태와 관계없이 화면 크기와 앱을 켤 때 정한 씨앗으로만 정해진다 — 연결·변경 모드·등록 여부가 바뀌어도 행성은 제자리 (레이아웃 안정성).
 */

/** 행성 차례(궤도 안쪽부터) ↔ 장비 1:1 (2026-10-10·11 사용자 결정) */
export const PLANET_OF: Record<string, number> = {
  flatdevice: 0, // 수성
  guider: 1, // 금성
  camera: 2, // 지구
  focuser: 3, // 화성
  switch: 4, // 목성
  mount: 5, // 토성
  rotator: 6, // 천왕성
  filterwheel: 7, // 해왕성
}

type Ring = 'saturn' | 'uranus'
/** 행성: 이름, 크기(지구 0.9 — 실제 지름 비를 크게 줄임), 고리 */
const PLANETS: { name: string; size: number; ring?: Ring }[] = [
  { name: '수성', size: 0.66 },
  { name: '금성', size: 0.8 },
  { name: '지구', size: 0.9 },
  { name: '화성', size: 0.78 },
  { name: '목성', size: 1.53 },
  { name: '토성', size: 1.25, ring: 'saturn' }, // 목성과의 차이(실제 약 1.19배)가 보이게 몸은 작게, 고리는 넓고 옅게
  { name: '천왕성', size: 1.1, ring: 'uranus' }, // 천왕성·해왕성은 느낌상 그리 크지 않게
  { name: '해왕성', size: 1.09 },
]
/** 고리 바깥 반지름 ÷ 행성 반지름 (차지하는 자리 계산용) */
const RING_REACH: Record<Ring, number> = { saturn: 2.05, uranus: 1.3 }
/** 고리 띠: [반지름 ÷ 행성 반지름, 굵기(1보다 작으면 행성 지름 비, 아니면 px), 진하기] */
const RING_BANDS: Record<Ring, [number, number, number][]> = {
  saturn: [[1.62, 0.32, 0.018], [1.3, 1.2, 0.13], [1.46, 1, 0.09], [1.72, 1.2, 0.07], [1.98, 1, 0.045]],
  uranus: [[1.24, 1.2, 0.16]],
}
/** 잘 알려진 위성만: [거리 ÷ 행성 반지름, 점 반지름 px] */
const MOONS: Record<string, [number, number][]> = {
  지구: [[1.95, 5]], // 달
  화성: [[1.45, 2.6], [1.78, 2.2]], // 포보스, 데이모스
  목성: [[1.32, 4.2], [1.56, 3.8], [1.86, 5.4], [2.24, 5]], // 이오, 유로파, 가니메데, 칼리스토
  토성: [[2.4, 5.4], [1.14, 2.4]], // 타이탄(고리 밖), 엔셀라두스(고리 안쪽)
  천왕성: [[1.66, 3.4], [1.96, 3.2]], // 티타니아, 오베론
  해왕성: [[1.66, 4.4]], // 트리톤
}

export interface Planet {
  x: number
  y: number
  /** 지름 */
  d: number
  /** 궤도 반지름 (항성 중심에서) */
  r: number
  /** 궤도 위 각도 (도) — 궤도를 행성 자리에서부터 그리려고 */
  a: number
  /** 화성 바깥(목성부터) */
  outer: boolean
  /** 목성: 대적점 */
  spot: boolean
  bands: { r: number; width: number; opacity: number }[]
  moons: { x: number; y: number; r: number }[]
}

export interface SolarGeo {
  /** 항성 중심(대부분 화면 밖)과 지름 */
  cx: number
  cy: number
  sunD: number
  /** 행성 차례(0~7)별 자리 */
  planets: Planet[]
  /** 소행성대 점 */
  belt: { x: number; y: number; r: number; o: number }[]
  /** 망원경 아이콘 자리 */
  scope: { x: number; y: number }
}

/** 앱을 켤 때 한 번 정하는 씨앗: 공전 시점(각도)은 앱을 다시 켤 때까지 같다 */
const sessionSeed = Math.floor(Math.random() * 2 ** 31)

/** 씨앗이 같으면 같은 수열 (0~1) */
function rng(seed: number) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

/** 망원경 아이콘의 반쯤 (아이콘 + 이름) */
const SCOPE_HALF = 56

export function solarLayout(w: number, h: number): SolarGeo | null {
  if (w < 200 || h < 200) return null
  // 기준 길이: 시안(1500×760) 비율로 — 세로가 긴 창에서 행성이 너무 커져 몰리지 않게
  const u = Math.min(h, w / 1.97)
  const sunD = u * 0.66
  const R = sunD / 2
  const base = u * 0.152 // 지구 지름 ÷ 0.9
  const cx = w * 0.015
  const cy = h * 0.02
  // 궤도 자리: 행성 8개 + 화성 뒤 소행성대 한 칸. 수성은 항성에 너무 붙지 않게 한 칸 바깥부터, 안쪽은 촘촘·바깥은 넓게
  const slots = PLANETS.length + 1
  const rFirst = R + base * 0.75
  const rMax = Math.hypot(w - cx - 90, h - cy - 90) * 0.94
  const rStart = rFirst + (rMax - rFirst) / (slots - 1)
  const slotR = (k: number) => rStart + (rMax - rStart) * Math.pow(k / (slots - 1), 1.45)

  const rand = rng(sessionSeed)
  const placed: { x: number; y: number; fp: number; d: number }[] = []
  const planets = PLANETS.map((p, i): Planet => {
    const d = base * p.size
    const moons = MOONS[p.name] ?? []
    // 차지하는 반지름: 고리·위성까지
    const fp = (d / 2) * Math.max(1, p.ring ? RING_REACH[p.ring] : 1, ...moons.map(([k]) => k + 0.1))
    const r = slotR(i >= 4 ? i + 1 : i)
    const m = fp + 14
    // 이 궤도에서 화면 안에 들어오는 각도 범위 (0 = 오른쪽, 90 = 아래). 가장자리에 잘리지 않게
    const lo = Math.max(Math.acos(Math.min(1, (w - cx - m) / r)), Math.asin(Math.min(1, Math.max(0, (m - cy) / r))))
    const hi = Math.min(Math.asin(Math.min(1, (h - cy - m) / r)), Math.acos(Math.min(1, Math.max(0, (m - cx) / r))))
    const loD = Math.max(3, (lo * 180) / Math.PI)
    const hiD = Math.min(87, (hi * 180) / Math.PI)
    let best = { a: (loD + hiD) / 2, x: 0, y: 0 }
    let bestScore = -Infinity
    for (let k = 0; k < 900; k++) {
      const a = loD + rand() * Math.max(0, hiD - loD)
      const th = (a * Math.PI) / 180
      const x = cx + r * Math.cos(th)
      const y = cy + r * Math.sin(th)
      // 모든 원끼리 고리·위성까지 겹치지 않게, 바로 안쪽 궤도의 행성과는 중심 거리가 지름(큰 쪽)의 2배 이상 (2026-10-10 사용자 규칙)
      const gap = Math.min(Infinity, ...placed.map((q) => Math.hypot(q.x - x, q.y - y) - q.fp - fp))
      const prev = placed[placed.length - 1]
      const near = prev ? Math.hypot(prev.x - x, prev.y - y) / (2 * Math.max(prev.d, d)) : 9
      const score = Math.min(gap / 22, near)
      if (score > bestScore) {
        bestScore = score
        best = { a, x, y }
      }
      if (gap > 22 && near >= 1) break
    }
    placed.push({ x: best.x, y: best.y, fp, d })
    const mr = rng(sessionSeed * 97 + i * 13)
    return {
      x: best.x,
      y: best.y,
      d,
      r,
      a: best.a,
      outer: i >= 4,
      spot: p.name === '목성',
      bands: (p.ring ? RING_BANDS[p.ring] : []).map(([k, wd, o]) => ({ r: (d / 2) * k, width: wd < 1 ? d * wd : wd, opacity: o })),
      moons: moons.map(([k, dot]) => {
        const t = mr() * Math.PI * 2
        return { x: best.x + (d / 2) * k * Math.cos(t), y: best.y + (d / 2) * k * Math.sin(t), r: dot }
      }),
    }
  })

  // 소행성대: 화성 궤도와 목성 궤도 사이 칸에 점 몇십 개
  const rb = slotR(4)
  const band = (slotR(5) - slotR(3)) * 0.16
  const br = rng(sessionSeed * 31 + 5)
  const belt = Array.from({ length: 70 }, () => {
    const th = ((-4 + br() * 100) * Math.PI) / 180
    const rr = rb + (br() + br() - 1) * band
    return { x: cx + rr * Math.cos(th), y: cy + rr * Math.sin(th), r: 0.6 + br() * 1.3, o: 0.12 + br() * 0.3 }
  })

  // 망원경: 내행성계~소행성대(목성 궤도 안쪽, 소행성대와 겹쳐도 됨)에서 가장 넓은 빈 곳 — 행성·위성·고리·항성·가장자리 모두에서 가장 먼 점
  const rOut = slotR(5)
  let scope = { x: cx + R * 1.3, y: cy + R * 0.6, clear: -Infinity }
  for (let x = SCOPE_HALF; x < w - SCOPE_HALF; x += 8)
    for (let y = SCOPE_HALF; y < h - SCOPE_HALF; y += 8) {
      const rr = Math.hypot(x - cx, y - cy)
      if (rr < R + SCOPE_HALF || rr > rOut - SCOPE_HALF) continue
      const clear = Math.min(rr - R, rOut - rr, x, y, w - x, h - y, ...placed.map((q) => Math.hypot(q.x - x, q.y - y) - q.fp))
      if (clear > scope.clear) scope = { x, y, clear }
    }

  return { cx, cy, sunD, planets, belt, scope: { x: scope.x, y: scope.y } }
}
