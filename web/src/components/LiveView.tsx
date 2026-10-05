import { useMemo, useRef } from 'react'
import type { CurrentView, ReadoutView } from '../prepare'
import styles from './LiveView.module.css'

/**
 * 하늘 화면 (DESIGN.md "촬영 준비" 화면 영역 — 배경 전체). 서버의 live.kind에 따라 그린다:
 * none · sharpcap · guide-image · sky-map · focus-curve · solved-photo · test-photo.
 * 지금은 모의 장비라 사진 대신 측정값(readout.values)과 live.data로 그림을 흉내 낸다.
 * 실제 장비(P3)에서는 live.url의 사진·창 캡처를 이 자리에 깐다.
 */

// 화면 영역 기준 좌표 (1920×1200 틀에서 상태 줄·진행 표시를 뺀 크기)
const VW = 1680
const VH = 1156
const FX = 840 // 하늘에서 보는 가운데 (글 자리를 피한 높이)
const FY = 470

export interface LiveExtras {
  /** 시험 사진 9칸 확대 보기 */
  grid?: boolean
}

export default function LiveView({ current, extras }: { current: CurrentView | null; extras?: LiveExtras }) {
  const live = current?.live ?? null
  const kind = live?.kind ?? 'none'
  const readout = current?.center.readout ?? null
  const slew = useSlewTrack(current)

  const dim = useMemo(() => starfield(260, 5, { dim: 0.35 }), [])
  const sparse = useMemo(() => starfield(70, 11, { scale: 1.8 }), [])
  const dense = useMemo(() => starfield(900, 99, { scale: 1.1 }), [])

  let body: React.ReactNode
  switch (kind) {
    case 'sharpcap':
      body = <SharpCap readout={readout} subStep={activeSub(current)} />
      break
    case 'guide-image':
      body = <GuideImage field={sparse} readout={readout} data={live?.data} subStep={activeSub(current)} />
      break
    case 'sky-map':
      body = <SkyMap field={dim} track={slew} label={current?.taskId === 'slew' ? '대상' : '캘리브레이션 위치'} />
      break
    case 'focus-curve':
      body = <FocusCurve field={dim} data={live?.data} readout={readout} />
      break
    case 'solved-photo':
      body = <SolvedPhoto readout={readout} solving={activeSub(current) === 'solve'} />
      break
    case 'test-photo':
      body = extras?.grid ? <NineGrid /> : <TestPhoto field={dense} />
      break
    default:
      body = <g>{dim}</g>
  }

  return (
    <div className={styles.view} aria-hidden="true">
      <svg viewBox={`0 0 ${VW} ${VH}`} preserveAspectRatio="xMidYMid slice">
        {body}
      </svg>
      {kind === 'guide-image' && <GuidingGraph data={live?.data} />}
    </div>
  )
}

function activeSub(current: CurrentView | null) {
  return current?.subSteps.find((s) => s.status === 'Running' || s.status === 'Waiting')?.id ?? null
}

// ── 그림 ─────────

function SharpCap({ readout, subStep }: { readout: ReadoutView | null; subStep: string | null }) {
  const stars = useMemo(() => polarStars(), [])
  const align = readout?.kind === 'polar-offset'
  // 극 찾기 동안은 별 자리를 천천히 돌려 "적경축을 돌리는 중"을 흉내 (각도는 상태 줄 문구에)
  const rot = align ? 90 : 0
  const a = (rot * Math.PI) / 180
  const tx = FX
  const ty = FY - 10
  const x = readout?.values.xPx ?? 0
  const y = readout?.values.yPx ?? 0
  const k = 2.4
  const px = clamp(tx + x * k, 120, VW - 120)
  const py = clamp(ty + y * k, 90, FY + 230) // 중앙 정보(등급 말)와 겹치지 않게
  return (
    <g>
      {stars.map((p, i) => {
        const sx = FX + (p.u * Math.cos(a) - p.v * Math.sin(a)) * 820
        const sy = 520 + (p.u * Math.sin(a) + p.v * Math.cos(a)) * 520
        return <circle key={i} className={styles.star} cx={sx} cy={sy} r={p.r * 1.3} opacity={p.b ? 0.85 : 0.45} />
      })}
      {!align && subStep === 'find' && stars.slice(0, 18).map((p, i) => (
        <circle key={`f${i}`} cx={FX + p.u * 820} cy={520 + p.v * 520} r={13} className={styles.mark} />
      ))}
      {align && (
        <g>
          <circle cx={tx} cy={ty} r={30} className={styles.target} />
          <circle cx={tx} cy={ty} r={4} className={styles.targetDot} />
          {Math.hypot(px - tx, py - ty) > 34 && <line x1={px} y1={py} x2={tx} y2={ty} className={styles.dash} />}
          <circle cx={px} cy={py} r={11} className={styles.accFill} />
          <text x={tx + 44} y={ty + 6} className={styles.label}>목표 위치</text>
        </g>
      )}
      <text x={FX} y={52} textAnchor="middle" className={styles.label}>SharpCap 화면 (모의)</text>
    </g>
  )
}

function GuideImage({ field, readout, data, subStep }: { field: React.ReactNode; readout: ReadoutView | null; data: unknown; subStep: string | null }) {
  // 캘리브레이션: 서쪽으로 n걸음 → 북쪽으로 n걸음, 별이 움직인 길을 그린다
  let star: { x: number; y: number } | null = null
  let path: string | null = null
  if (readout?.kind === 'steps') {
    const n = readout.values.step ?? 0
    const north = readout.big.startsWith('북')
    const pts: string[] = []
    const west = north ? 12 : n
    for (let i = 0; i <= west; i++) pts.push(`${FX + i * 18},${FY - i * 1.2}`)
    if (north) for (let i = 1; i <= n; i++) pts.push(`${FX + 216 + i * 1.3},${FY - 14 - i * 17}`)
    path = pts.length > 1 ? 'M' + pts.join(' L') : null
    star = north ? { x: FX + 216 + n * 1.3, y: FY - 14 - n * 17 } : { x: FX + n * 18, y: FY - n * 1.2 }
  } else if ((readout && ['star', 'calibration', 'guiding'].includes(readout.kind)) || subStep === 'settle' || subStep === 'measure') star = { x: FX, y: FY }
  const guiding = readout?.kind === 'guiding' || hasSeries(data)
  return (
    <g>
      {field}
      {path && <path d={path} className={styles.trail} />}
      {star && (
        <g className={guiding ? styles.jitter : undefined}>
          <circle cx={star.x} cy={star.y} r={30} className={styles.star} opacity={0.12} />
          <circle cx={star.x} cy={star.y} r={14} className={styles.star} opacity={0.35} />
          <circle cx={star.x} cy={star.y} r={7} className={styles.star} />
          <rect x={star.x - 40} y={star.y - 40} width={80} height={80} rx={4} className={styles.box} />
        </g>
      )}
      {!star && <text x={FX} y={FY} textAnchor="middle" className={styles.label}>별을 찾는 중</text>}
      <text x={FX} y={52} textAnchor="middle" className={styles.label}>가이드 카메라 (PHD2)</text>
    </g>
  )
}

function hasSeries(data: unknown): data is { series: [number, number][]; limit: number } {
  return !!data && typeof data === 'object' && Array.isArray((data as { series?: unknown }).series)
}

/** 가이딩 그래프: 가이드 별 아래, 중앙 정보 위 */
function GuidingGraph({ data }: { data: unknown }) {
  if (!hasSeries(data) || data.series.length < 2) return null
  const W = 650
  const H = 190
  const x0 = 20
  const x1 = W - 20
  const gy = 108
  const gh = 120
  const n = 70
  const lim = data.limit || 2.4
  const s = data.series.slice(-n)
  const line = (pick: (p: [number, number]) => number) =>
    s.map((p, i) => `${i ? 'L' : 'M'}${(x0 + ((x1 - x0) * i) / (n - 1)).toFixed(1)},${(gy - (clamp(pick(p), -lim * 1.4, lim * 1.4) * gh) / 2 / (lim * 1.4)).toFixed(1)}`).join('')
  return (
    <div className={styles.graph}>
      <svg viewBox={`0 0 ${W} ${H}`}>
        <line x1={x0} y1={gy} x2={x1} y2={gy} className={styles.axis} />
        <line x1={x0} y1={gy - gh / 2.8} x2={x1} y2={gy - gh / 2.8} className={styles.limit} />
        <line x1={x0} y1={gy + gh / 2.8} x2={x1} y2={gy + gh / 2.8} className={styles.limit} />
        <path d={line((p) => p[1])} className={styles.dec} />
        <path d={line((p) => p[0])} className={styles.ra} />
        <text x={x1} y={24} textAnchor="end" className={styles.small}>
          — 적경  — 적위 · 기준 ±{lim.toFixed(2)}″
        </text>
      </svg>
    </div>
  )
}

interface SlewTrack {
  /** 0 = 출발, 1 = 도착 */
  progress: number
  targetAlt: number
}

/** 대상 이동: 남은 거리(readout)로 적도의 점의 위치를 정한다 — 처음 본 거리를 출발로 */
function useSlewTrack(current: CurrentView | null): SlewTrack {
  const start = useRef<{ run: number; dist: number } | null>(null)
  const alt = useRef(42)
  const r = current?.center.readout
  const runId = current?.runId ?? 0
  if (current?.taskId !== 'slew') start.current = null
  if (r?.values.altitudeDeg !== undefined) alt.current = r.values.altitudeDeg
  let progress = 0
  if (r?.kind === 'distance') {
    if (r.values.arrivalDeg !== undefined) progress = 1
    else if (r.values.remainingDeg !== undefined) {
      const d = r.values.remainingDeg
      if (!start.current || start.current.run !== runId || d > start.current.dist) start.current = { run: runId, dist: d }
      progress = start.current.dist > 0 ? 1 - d / start.current.dist : 1
    }
  }
  return { progress, targetAlt: alt.current }
}

function SkyMap({ field, track, label }: { field: React.ReactNode; track: SlewTrack; label: string }) {
  const CX = FX
  const CY = FY + 20
  const RR = 310
  const pos = (alt: number, az: number) => {
    const rr = (RR * (90 - alt)) / 90
    const a = ((az - 90) * Math.PI) / 180
    return { x: CX + rr * Math.cos(a), y: CY + rr * Math.sin(a) }
  }
  const tgt = { alt: track.targetAlt, az: 64 }
  const from = { alt: 50, az: 203 }
  const e = 1 - Math.pow(1 - track.progress, 2.2)
  const now = { alt: from.alt + (tgt.alt - from.alt) * e, az: from.az + (tgt.az - from.az) * e }
  const trail: string[] = []
  for (let i = 0; i <= 24; i++) {
    const f = (i / 24) * track.progress
    const ee = 1 - Math.pow(1 - f, 2.2)
    const p = pos(from.alt + (tgt.alt - from.alt) * ee, from.az + (tgt.az - from.az) * ee)
    trail.push(`${p.x.toFixed(1)},${p.y.toFixed(1)}`)
  }
  const t = pos(tgt.alt, tgt.az)
  const s = pos(now.alt, now.az)
  return (
    <g>
      {field}
      <circle cx={CX} cy={CY} r={RR} className={styles.grid} />
      <circle cx={CX} cy={CY} r={(RR * 2) / 3} className={styles.grid} strokeDasharray="5 8" />
      <circle cx={CX} cy={CY} r={RR / 3} className={styles.grid} />
      <text x={CX} y={CY - RR - 14} textAnchor="middle" className={styles.label}>북</text>
      <text x={CX + RR + 16} y={CY + 6} className={styles.label}>동</text>
      <text x={CX} y={CY + RR + 30} textAnchor="middle" className={styles.label}>남</text>
      <text x={CX - RR - 16} y={CY + 6} textAnchor="end" className={styles.label}>서</text>
      <text x={CX + 8} y={CY - (RR * 2) / 3 - 8} className={styles.label}>30°</text>
      {track.progress > 0 && <path d={'M' + trail.join(' L')} className={styles.trailDim} />}
      <circle cx={t.x} cy={t.y} r={20} className={styles.acc} />
      <text x={t.x + 28} y={t.y + 6} className={styles.labelStrong}>{label}</text>
      <circle cx={s.x} cy={s.y} r={10} className={styles.accFill} />
    </g>
  )
}

function FocusCurve({ field, data, readout }: { field: React.ReactNode; data: unknown; readout: ReadoutView | null }) {
  const d = (data ?? {}) as { points?: { pos: number; hfr: number }[]; last?: number | null }
  const pts = d.points ?? []
  const X0 = 380
  const X1 = 1300
  const Y0 = 700
  const Y1 = 300
  const lo = Math.min(...pts.map((p) => p.pos), d.last ?? Infinity)
  const hi = Math.max(...pts.map((p) => p.pos), d.last ?? -Infinity)
  const span = Number.isFinite(lo) && Number.isFinite(hi) && hi > lo ? [lo, hi] : [0, 1]
  const hmax = Math.max(4, ...pts.map((p) => p.hfr)) + 0.5
  const hmin = Math.max(0, Math.min(...pts.map((p) => p.hfr), 2) - 0.5)
  const px = (p: number) => X0 + ((X1 - X0) * (p - span[0])) / (span[1] - span[0])
  const py = (h: number) => Y0 - ((Y0 - Y1) * (Math.min(h, hmax) - hmin)) / (hmax - hmin)
  const done = readout?.kind === 'hfr' && readout.tone === 'Ok'
  const fit = done && pts.length >= 3 ? quadFit(pts.map((p) => [p.pos, p.hfr])) : null
  const best = done ? readout?.values.position ?? (fit ? -fit[1] / (2 * fit[0]) : null) : null
  let curve = ''
  if (fit) for (let t = 0; t <= 40; t++) {
    const p = span[0] + ((span[1] - span[0]) * t) / 40
    curve += `${t ? 'L' : 'M'}${px(p).toFixed(1)},${py(fit[0] * p * p + fit[1] * p + fit[2]).toFixed(1)}`
  }
  return (
    <g>
      {field}
      <line x1={X0} y1={Y0} x2={X1} y2={Y0} className={styles.grid} />
      <line x1={X0} y1={Y0} x2={X0} y2={Y1} className={styles.grid} />
      <text x={X0} y={Y0 + 32} className={styles.label}>포커서 위치</text>
      <text x={X0 - 12} y={Y1 + 6} textAnchor="end" className={styles.label}>별 크기</text>
      {d.last != null && pts.length > 0 && (
        <g>
          <line x1={px(d.last)} y1={Y0} x2={px(d.last)} y2={Y1 + 20} className={styles.trailDim} />
          <text x={px(d.last)} y={Y1 + 10} textAnchor="middle" className={styles.label}>지난번 {d.last.toLocaleString()}</text>
        </g>
      )}
      {curve && <path d={curve} className={styles.acc} opacity={0.8} />}
      {best != null && fit && (
        <g>
          <line x1={px(best)} y1={Y0} x2={px(best)} y2={py(fit[0] * best * best + fit[1] * best + fit[2])} className={styles.accDash} />
          <text x={px(best)} y={Y0 + 32} textAnchor="middle" className={styles.labelAcc}>{Math.round(best).toLocaleString()}</text>
        </g>
      )}
      {pts.map((p, i) => (
        <circle key={i} cx={px(p.pos)} cy={py(p.hfr)} r={8} className={i === pts.length - 1 && !done ? styles.accFill : styles.star} />
      ))}
      {pts.length === 0 && <text x={(X0 + X1) / 2} y={(Y0 + Y1) / 2} textAnchor="middle" className={styles.label}>포커서를 옮기는 중</text>}
    </g>
  )
}

function SolvedPhoto({ readout, solving }: { readout: ReadoutView | null; solving: boolean }) {
  const err = readout?.kind === 'center-error' ? readout.values.errorArcmin ?? 0 : null
  const off = err === null ? { x: 300, y: -170 } : { x: Math.min(err, 14) * 24, y: -Math.min(err, 14) * 13.7 }
  const field = useMemo(() => starfield(620, 31 + Math.round(off.x)), [off.x])
  const cx = FX + off.x
  const cy = FY + off.y
  return (
    <g>
      {field}
      {galaxy(cx, cy, 2.2)}
      <line x1={FX - 40} y1={FY} x2={FX + 40} y2={FY} className={styles.cross} />
      <line x1={FX} y1={FY - 40} x2={FX} y2={FY + 40} className={styles.cross} />
      <circle cx={cx} cy={cy} r={42} className={styles.acc} />
      <text x={FX} y={FY + 300} textAnchor="middle" className={styles.label}>
        {solving ? '사진에서 위치를 계산하는 중' : '+ 화면 가운데 · ○ 대상'}
      </text>
    </g>
  )
}

function TestPhoto({ field }: { field: React.ReactNode }) {
  return (
    <g>
      {field}
      {galaxy(FX, FY, 1.8)}
    </g>
  )
}

/** 9칸 확대 보기: 가운데 + 네 귀퉁이·네 변 (귀퉁이 별이 길쭉하면 기울어짐·백포커스) */
function NineGrid() {
  const CW = 180
  const G = 12
  const X0 = FX - (CW * 1.5 + G)
  const Y0 = FY - (CW * 1.5 + G) + 70 // 안내 글 아래, 중앙 정보 위
  const names = ['왼쪽 위', '위', '오른쪽 위', '왼쪽', '가운데', '오른쪽', '왼쪽 아래', '아래', '오른쪽 아래']
  return (
    <g>
      {names.map((n, k) => {
        const cx = X0 + (k % 3) * (CW + G)
        const cy = Y0 + Math.floor(k / 3) * (CW + G)
        const r = rng(300 + k)
        return (
          <g key={n}>
            <rect x={cx} y={cy} width={CW} height={CW} rx={6} className={styles.cell} />
            {Array.from({ length: 9 }, (_, j) => {
              const m = r()
              return <circle key={j} cx={cx + 20 + r() * (CW - 40)} cy={cy + 20 + r() * (CW - 40)} r={m > 0.7 ? 7 : m > 0.35 ? 4.5 : 3} className={styles.star} />
            })}
            {k === 4 && galaxy(cx + CW / 2, cy + CW / 2, 0.8)}
            <text x={cx + 12} y={cy + CW - 12} className={styles.small}>{n}</text>
          </g>
        )
      })}
    </g>
  )
}

// ── 도우미 ─────────

function clamp(v: number, a: number, b: number) {
  return Math.max(a, Math.min(b, v))
}

function rng(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function starfield(n: number, seed: number, opts: { dim?: number; scale?: number } = {}) {
  const r = rng(seed)
  const k = opts.scale ?? 1
  return (
    <g>
      {Array.from({ length: n }, (_, i) => {
        const m = r()
        const rad = (m > 0.985 ? 3.4 : m > 0.93 ? 2.1 : m > 0.7 ? 1.4 : 0.9) * k
        return <circle key={i} className={styles.star} cx={(r() * VW).toFixed(1)} cy={(r() * VH).toFixed(1)} r={rad.toFixed(2)} opacity={((opts.dim ?? 1) * (0.35 + m * 0.65)).toFixed(2)} />
      })}
    </g>
  )
}

function polarStars() {
  const r = rng(77)
  return Array.from({ length: 46 }, () => {
    const m = r()
    return { u: r() * 2 - 1, v: r() * 2 - 1, r: m > 0.9 ? 4.2 : m > 0.6 ? 2.6 : 1.8, b: m > 0.45 }
  })
}

function galaxy(cx: number, cy: number, k: number) {
  return (
    <g transform={`translate(${cx} ${cy}) rotate(-38) scale(${k})`} opacity={0.9}>
      <ellipse rx={150} ry={42} className={styles.star} opacity={0.08} />
      <ellipse rx={105} ry={28} className={styles.star} opacity={0.12} />
      <ellipse rx={60} ry={16} className={styles.star} opacity={0.2} />
      <ellipse rx={22} ry={8} className={styles.star} opacity={0.55} />
    </g>
  )
}

/** 2차 곡선 맞춤 (초점 곡선): [a, b, c] — y = a x² + b x + c */
function quadFit(p: [number, number][]): [number, number, number] | null {
  const n = p.length
  const mx = p.reduce((s, q) => s + q[0], 0) / n
  let s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0
  for (const [x0, y] of p) {
    const x = x0 - mx
    s0 += 1; s1 += x; s2 += x * x; s3 += x * x * x; s4 += x * x * x * x
    t0 += y; t1 += x * y; t2 += x * x * y
  }
  const det = (m: number[][]) =>
    m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0])
  const M = [[s4, s3, s2], [s3, s2, s1], [s2, s1, s0]]
  const D = det(M)
  if (Math.abs(D) < 1e-12) return null
  const a = det([[t2, s3, s2], [t1, s2, s1], [t0, s1, s0]]) / D
  const b = det([[s4, t2, s2], [s3, t1, s1], [s2, t0, s0]]) / D
  const c = det([[s4, s3, t2], [s3, s2, t1], [s2, s1, t0]]) / D
  if (a <= 0) return null
  // 가운데로 옮긴 x를 되돌린다
  return [a, b - 2 * a * mx, a * mx * mx - b * mx + c]
}
