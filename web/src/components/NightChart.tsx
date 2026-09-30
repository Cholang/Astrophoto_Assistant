import type { Chart } from '../plan'
import styles from './NightChart.module.css'

// 그래프 좌표 (viewBox 800×136). 위에서부터: 고도 0~90°, 달, 구름, 시각
const L = 34
const W = 756
const TOP = 6
const H = 90
const MOON_Y = 101
const CLOUD_Y = 110

/**
 * 오늘 밤 조건 한 장: 어두운 시간(양 끝 옅은 면), 대상 고도 곡선 + 30° 기준선, 자오선, 달 떠 있는 시간,
 * 시간별 구름, 계획이 정해지면 촬영 시간대 띠. [재검토] DESIGN.md — 해석이 어렵다는 의견, 형식은 나중에 다시 본다.
 */
export default function NightChart({ chart }: { chart: Chart }) {
  const x = (min: number) => L + (Math.max(0, Math.min(chart.minutes, min)) / chart.minutes) * W
  const y = (alt: number) => TOP + (90 - Math.max(0, Math.min(90, alt)))
  const hourW = W / (chart.minutes / 60)

  const track = chart.target?.track ?? []
  const path = track.map(([m, a], i) => `${i ? 'L' : 'M'}${x(m).toFixed(1)},${y(a).toFixed(1)}`).join('')
  // 이름표: 곡선이 가장 높은 곳 조금 왼쪽
  const peak = track.reduce<[number, number] | null>((best, p) => (!best || p[1] > best[1] ? p : best), null)

  return (
    <svg className={styles.chart} viewBox="0 0 800 136" role="img" aria-label={`오늘 밤 조건. 어두운 시간 ${chart.dark.label}`}>
      <rect x={x(0)} y={TOP} width={x(chart.dark.start) - x(0)} height={H} className={styles.twilight} />
      <rect x={x(chart.dark.end)} y={TOP} width={x(chart.minutes) - x(chart.dark.end)} height={H} className={styles.twilight} />
      {chart.shooting && (
        <rect x={x(chart.shooting.start)} y={TOP} width={x(chart.shooting.end) - x(chart.shooting.start)} height={H} className={styles.shooting} />
      )}

      {[0, 30, 60, 90].map((a) => (
        <g key={a}>
          <line x1={L} x2={L + W} y1={y(a)} y2={y(a)} className={a === 30 ? styles.guide : styles.grid} />
          <text x={L - 6} y={y(a) + 4} textAnchor="end">{a}°</text>
        </g>
      ))}

      {chart.target && (
        <>
          <path d={path} className={styles.track} />
          {chart.target.transit !== null && (
            <>
              <line x1={x(chart.target.transit)} x2={x(chart.target.transit)} y1={TOP} y2={TOP + H} className={styles.transit} />
              <text x={x(chart.target.transit) - 4} y={18} textAnchor="end">자오선 {chart.target.transitLabel}</text>
            </>
          )}
          {peak && peak[1] > 5 && (
            <text x={x(peak[0] - 150)} y={y(peak[1] * 0.75) - 6} className={styles.label}>
              {chart.target.name}
            </text>
          )}
        </>
      )}

      {chart.moon.up.map((u) => (
        <g key={u.start}>
          <rect x={x(u.start)} y={MOON_Y} width={Math.max(2, x(u.end) - x(u.start))} height={4} rx={2} className={styles.moon} />
          {u.end < chart.minutes - 30 ? (
            <text x={x(u.end) + 6} y={MOON_Y + 5}>달 {u.setLabel} 짐</text>
          ) : u.start > 30 ? (
            <text x={x(u.start) - 6} y={MOON_Y + 5} textAnchor="end">달 {u.riseLabel} 뜸</text>
          ) : null}
        </g>
      ))}

      {chart.clouds.map((c) => (
        <rect
          key={c.at}
          x={x(c.at) + 1}
          y={CLOUD_Y}
          width={Math.max(0, Math.min(hourW, x(chart.minutes) - x(c.at)) - 2)}
          height={9}
          rx={2}
          className={styles.cloud}
          style={{ opacity: 0.06 + (c.cover / 100) * 0.9 }}
        >
          <title>
            구름 {c.cover}%
          </title>
        </rect>
      ))}
      <text x={L - 6} y={CLOUD_Y + 8} textAnchor="end">구름</text>

      {chart.hours
        .filter((_, i) => i % 2 === 0)
        .map((h) => (
          <text key={h.at} x={x(h.at)} y={133} textAnchor="middle">
            {h.label}시
          </text>
        ))}
    </svg>
  )
}
