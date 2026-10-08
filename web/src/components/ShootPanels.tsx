import { Check } from 'lucide-react'
import { clock, hm, type ShootView } from '../shoot'
import styles from './ShootPanels.module.css'

/**
 * 촬영 화면 조각 (DESIGN.md "촬영", 시안 mockups/aa-shoot-wrap-v10.html).
 * 계기판: 가이딩 오차 · 진행(크게) · 별 크기. 등급: 왼쪽 아래 — 지금 등급 + 등급별 누적 장수(세로), 등급에 올리면 분류 기준.
 */

const GRADES = ['A+', 'A', 'B', 'C', 'F'] as const

function Spark({ values, lo, hi, count }: { values: number[]; lo: number; hi: number; count: number }) {
  const w = 300
  const h = 58
  const pts = values.slice(-count)
  const step = w / (count - 1)
  const off = (count - pts.length) * step
  const d = pts
    .map((v, i) => `${i ? 'L' : 'M'}${(off + i * step).toFixed(1)},${(2 + h - ((Math.min(hi, Math.max(lo, v)) - lo) / (hi - lo)) * h).toFixed(1)}`)
    .join('')
  return (
    <svg viewBox="0 0 300 62" aria-hidden="true">
      <line className={styles.gl} x1="0" y1="61" x2="300" y2="61" />
      {d && <path className={styles.gp} d={d} />}
    </svg>
  )
}

/** 가이딩 판정: 주 카메라 한 픽셀 이하 충분 / 1.5배 이하 지켜보기 / 그 위 아쉬움 (준비 ⑥과 같은 기준) */
function guideWord(rms: number, pixel: number) {
  return rms <= pixel ? '충분해요' : rms <= pixel * 1.5 ? '지켜볼게요' : '아쉬워요'
}

/** 이슬 여유와 열선 (열선은 WandererEmpire 자동이 맡음): "이슬 여유 16°C · 열선 0% (자동)" */
function dewText(d: ShootView['dew']) {
  if (!d) return null
  const parts: string[] = []
  if (d.marginC !== null) parts.push(`이슬 여유 ${d.marginC.toFixed(0)}°C`)
  if (d.heaterPower !== null) parts.push(`열선 ${Math.round(d.heaterPower * 100)}%${d.heaterAuto ? ' (자동)' : ''}`)
  return parts.length ? parts.join(' · ') : null
}

export function ShootGauges({ v }: { v: ShootView }) {
  const g = v.guide.at(-1)
  const h = v.hfr.at(-1)
  const pct = v.planned ? Math.min(100, (v.good / v.planned) * 100) : 0
  const exp =
    v.mode === 'Dither'
      ? '디더링 · 가이딩 안정 기다리는 중'
      : v.mode === 'Shoot' || v.mode === 'Finishing'
        ? `노출 중 · ${v.elapsed} / ${v.exposureSeconds}초 · ISO ${v.iso}`
        : v.mode === 'Paused'
          ? `촬영 멈춤 · ${Math.floor(v.pausedSeconds / 60)}분 ${v.pausedSeconds % 60}초째`
          : '촬영 멈춤'
  const flip = v.flipAt && v.flipInMinutes ? `자오선 반전 ${clock(v.flipAt)} (${v.flipInMinutes}분 뒤, 자동)` : null
  const dew = dewText(v.dew)
  return (
    <div className={styles.gauges}>
      <div className={styles.gauge}>
        <Spark values={v.guide} lo={0} hi={Math.max(1.6, v.pixelScale)} count={48} />
        <b>{g !== undefined ? `${g.toFixed(1)}″` : '—'}</b>
        <span>가이딩 오차{g !== undefined ? ` · ${guideWord(g, v.pixelScale)}` : ''}</span>
      </div>
      <div className={styles.prog}>
        <strong>
          {v.good} <small>/ {v.planned}장</small>
        </strong>
        <span className={styles.exp}>{exp}</span>
        <div className={styles.meter}>
          <i style={{ width: `${pct.toFixed(1)}%` }} />
        </div>
        <span>
          누적 {hm(v.good * v.exposureSeconds)}
          {v.endAt ? ` · 끝 예상 ${clock(v.endAt)}` : ''}
          {flip ? ` · ${flip}` : ''}
          {dew ? ` · ${dew}` : ''}
        </span>
      </div>
      <div className={styles.gauge}>
        <Spark values={v.hfr} lo={1.6} hi={Math.max(3.2, v.focusHfr * 1.6)} count={20} />
        <b>{h !== undefined ? h.toFixed(1) : '—'}</b>
        <span>별 크기(HFR) · 초점 때 {v.focusHfr.toFixed(1)}</span>
      </div>
    </div>
  )
}

export function ShootGrades({ v }: { v: ShootView }) {
  if (!v.lastGrade && !Object.values(v.tally).some((n) => n > 0)) return null
  return (
    <div className={styles.score}>
      <span className={styles.grade} data-x={v.lastGrade === 'F'}>
        {v.lastGrade ?? ''}
      </span>
      <ul className={styles.tally} aria-label="등급별 누적 장수">
        {GRADES.map((g) => (
          <li key={g} data-x={g === 'F'} data-now={g === v.lastGrade}>
            <b tabIndex={0} data-tip={v.criteria[g] ?? ''} aria-label={`${g} ${v.tally[g] ?? 0}장. ${v.criteria[g] ?? ''}`}>
              {g}
            </b>
            <span>{v.tally[g] ?? 0}장</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** 자오선 반전: 반전 → 다시 센터링 → 가이딩 재시작 */
export function FlipSteps({ step }: { step: number }) {
  const steps = ['반전', '다시 센터링', '가이딩 재시작']
  return (
    <div className={styles.flip}>
      <div className={styles.seq}>
        {steps.map((s, k) => {
          const st = k < step ? 'done' : k === step ? 'now' : 'todo'
          return (
            <span key={s} data-s={st}>
              {st === 'done' && <Check strokeWidth={2.5} aria-hidden="true" />}
              {s}
            </span>
          )
        })}
      </div>
      <span>반전 뒤 사진은 180° 돌아가 찍혀요 · 스태킹 때 별 위치로 맞춰져요</span>
    </div>
  )
}
