import { ChevronLeft, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'
import type { PrepAction, ReadoutView, Tone } from '../prepare'
import StatusIcon, { type Status } from './StatusIcon'
import styles from './PrepCenter.module.css'

/**
 * 중앙 정보 (DESIGN.md "촬영 준비" — 가운데 아래): 측정값 → 상태 줄 → 버튼. 순서·자리 고정, 모든 작업 공통 껍데기.
 * 작업마다 다른 건 측정값 칸 안의 그림뿐 (① 등급·화살표, 나머지는 큰 숫자 + 설명).
 * 주 버튼은 가운데 고정, 보조 버튼은 그 오른쪽에 같은 높이로 — 보조 버튼 수와 관계없이 주 버튼이 움직이지 않는다.
 */
export default function PrepCenter({
  readout,
  status,
  actions,
  onAct,
  disabled,
  custom,
}: {
  readout: ReadoutView | null
  /** 측정값 칸에 화면이 직접 그리는 것 (촬영의 계기판 등) */
  custom?: ReactNode
  status: { text: string; tone: Tone } | null
  actions: PrepAction[]
  onAct: (id: string) => void
  disabled?: boolean
}) {
  // 다크 장수 고르기: 줄이기·늘리기는 숫자 좌우 쉐브론으로 (버튼 줄에 두지 않음)
  const stepper = readout?.kind === 'dark-count'
  const shown = stepper ? actions.filter((a) => a.id !== 'less' && a.id !== 'more') : actions
  const primary = shown.find((a) => a.primary) ?? shown[0]
  const rest = shown.filter((a) => a !== primary)
  return (
    <section className={styles.center} aria-live="polite">
      <div className={styles.readout} data-fresh={readout?.freshness ?? 'Fresh'}>
        {custom ??
          (readout &&
            (readout.kind === 'polar-offset' ? (
              <PolarReadout r={readout} />
            ) : stepper ? (
              <Stepper r={readout} onAct={onAct} disabled={disabled || !actions.some((a) => a.id === 'less')} />
            ) : (
              <Metric r={readout} />
            )))}
      </div>

      <p className={styles.status} data-tone={status?.tone ?? 'None'}>
        {status && status.tone !== 'None' && <StatusIcon status={TONE[status.tone]} />}
        <span>{status?.text ?? ''}</span>
      </p>

      <div className={styles.actions} data-empty={!primary}>
        {primary && (
          <button type="button" className={styles.primary} onClick={() => onAct(primary.id)} disabled={disabled}>
            {primary.label}
          </button>
        )}
        <div className={styles.secondary}>
          {rest.map((a) => (
            <button key={a.id} type="button" className={styles.quiet} onClick={() => onAct(a.id)} disabled={disabled}>
              {a.label}
            </button>
          ))}
        </div>
      </div>
    </section>
  )
}

const TONE: Record<Tone, Status> = { None: 'Pending', Busy: 'Running', Ok: 'Pass', Warn: 'Warn', Fail: 'Fail' }

function freshNote(r: ReadoutView) {
  return r.freshness === 'Stale' ? ' · 값이 갱신되지 않습니다' : ''
}

/** 큰 숫자 + 설명 (목표를 향해 가는 숫자만 크게) */
function Metric({ r }: { r: ReadoutView }) {
  return (
    <div className={styles.metric}>
      <strong data-tone={r.tone}>{r.big}</strong>
      <span>
        {r.caption}
        {freshNote(r)}
      </span>
    </div>
  )
}

/** 다크 장수: 숫자 좌우에 줄이기·늘리기 (기본 20, 5장씩 5~40 — 서버가 범위를 지킴) */
function Stepper({ r, onAct, disabled }: { r: ReadoutView; onAct: (id: string) => void; disabled?: boolean }) {
  const n = r.values.count ?? 0
  return (
    <div className={styles.metric}>
      <div className={styles.stepper}>
        <button type="button" onClick={() => onAct('less')} disabled={disabled || n <= (r.values.min ?? 0)} aria-label="5장 줄이기">
          <ChevronLeft strokeWidth={2.5} aria-hidden="true" />
        </button>
        <strong>
          {r.big}
          <small>장</small>
        </strong>
        <button type="button" onClick={() => onAct('more')} disabled={disabled || n >= (r.values.max ?? 99)} aria-label="5장 늘리기">
          <ChevronRight strokeWidth={2.5} aria-hidden="true" />
        </button>
      </div>
      <span>{r.caption}</span>
    </div>
  )
}

/** ① 극축 정렬: 등급 말을 크게 + 방위·고도 나사 방향과 양 + 극축 오차 */
function PolarReadout({ r }: { r: ReadoutView }) {
  const x = r.values.xPx ?? 0
  const y = r.values.yPx ?? 0
  const verified = r.freshness !== 'Unverified' && r.values.errorArcmin !== undefined
  const amount = (arcmin: number | undefined, px: number) => (verified && arcmin !== undefined ? `${arcmin.toFixed(1)}′` : `${Math.abs(px).toFixed(0)}px`)
  return (
    <div className={styles.polar}>
      <div className={styles.grade} data-tone={r.tone}>
        {verified ? r.big : '조절 중'}
      </div>
      <div className={styles.dirs}>
        <div>
          <b>{x > 0 ? '←' : '→'}</b>
          <span>{x > 0 ? '왼쪽' : '오른쪽'}</span>
          <strong>{amount(r.values.xArcmin, x)}</strong>
        </div>
        <i className={styles.cross} aria-hidden="true">＋</i>
        <div>
          <b>{y > 0 ? '↓' : '↑'}</b>
          <span>{y > 0 ? '아래' : '위'}</span>
          <strong>{amount(r.values.yArcmin, y)}</strong>
        </div>
      </div>
      <p>
        {r.caption}
        {freshNote(r)}
      </p>
    </div>
  )
}
