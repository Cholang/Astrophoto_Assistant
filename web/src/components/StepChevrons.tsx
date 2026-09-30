import { Check, RotateCw } from 'lucide-react'
import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import type { Status } from './StatusIcon'
import styles from './StepChevrons.module.css'

export interface ChevronStep {
  id: string
  title: string
  /** 이름 아래 작게 (예: 장비 종류 "적도의" 아래의 장비 이름 "OnStep") */
  term?: string | null
  status: Status
}

/** 칸 수가 달라도 칸 크기는 같게: 항상 이 칸 수 기준으로 너비를 나누고, 남는 오른쪽은 비워 둔다 */
const SLOTS = 5

const STATUS_WORD: Record<Status, string> = {
  Pending: '대기',
  Running: '확인 중',
  Pass: '통과',
  Fail: '필요',
  Warn: '권장',
  Skipped: '보류',
  Absent: '없음',
}

/**
 * 한 단계 안의 하위 항목을 화면 폭을 가득 채우는 큰 화살표 띠로 보여 준다 (설치 확인, 엔진 켜기 등).
 * 칸에는 이름만 쓰고, 통과하면 이름 아래 가운데에 큰 체크 표시. 실패 표시는 칸에 두지 않는다.
 * 이름은 칸의 세로 정중앙에 고정 — 체크 자리를 미리 확보해 두어 체크가 생겨도 이름이 움직이지 않는다.
 * 현재 칸은 채우고 120%로 키운다 (전환은 애니메이션). 칸을 누르면 그 항목이 현재가 된다.
 * onRetry가 있으면 현재 칸 안에 새로고침(다시 확인) 버튼을 둔다.
 */
export default function StepChevrons({
  steps,
  current,
  onSelect,
  onRetry,
  disabled = false,
  label,
}: {
  steps: ChevronStep[]
  current: string
  onSelect: (id: string) => void
  onRetry?: () => void
  /** 더 고를 수 없는 상태 (모두 통과해 다음 단계만 남음). 칸은 초점도 받지 않는다 */
  disabled?: boolean
  label: string
}) {
  const size = useCellSize()

  return (
    <ol
      className={styles.strip}
      aria-label={label}
      ref={size.ref}
      data-disabled={disabled}
      style={{ '--slots': Math.max(SLOTS, steps.length) } as CSSProperties}
    >
      {steps.map((s, i) => {
        const isCurrent = s.id === current
        return (
          <li key={s.id} className={styles.item} data-status={s.status} data-current={isCurrent}>
            {size.w > 0 && (
              <svg className={styles.shape} width={size.w} height={size.h} aria-hidden="true">
                <path d={chevronPath(size.w, size.h, i === 0, size.notch)} />
              </svg>
            )}
            <button
              type="button"
              className={styles.select}
              aria-current={isCurrent ? 'step' : undefined}
              disabled={disabled}
              aria-label={`${i + 1}. ${s.title}${s.term ? ` ${s.term}` : ''}: ${STATUS_WORD[s.status]}`}
              onClick={() => onSelect(s.id)}
            >
              <span className={styles.title}>
                {s.title}
                {s.term && <small className={styles.sub}>{s.term}</small>}
              </span>
              <span className={styles.number} aria-hidden="true">
                {i + 1}
              </span>
              {/* 체크 자리는 처음부터 확보해 두고 통과하면 보이기만 한다 — 이름 위치가 움직이지 않게 */}
              <Check className={styles.check} data-shown={s.status === 'Pass'} strokeWidth={2.5} aria-hidden="true" />
            </button>
            {isCurrent && onRetry && (
              <button type="button" className={styles.retry} aria-label="다시 확인" title="다시 확인" onClick={onRetry}>
                <RotateCw strokeWidth={2} aria-hidden="true" />
              </button>
            )}
          </li>
        )
      })}
    </ol>
  )
}

/** 칸 하나의 크기를 재서 화살표 모양을 그 크기에 맞게 그린다 (모든 칸은 같은 크기). */
function useCellSize() {
  const ref = useRef<HTMLOListElement>(null)
  const [size, setSize] = useState({ w: 0, h: 0, notch: 0 })

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => {
      const cell = el.querySelector('li')
      if (!cell) return
      const w = cell.clientWidth
      const h = cell.clientHeight
      const notch = parseFloat(getComputedStyle(el).getPropertyValue('--notch-px')) || 24
      setSize((s) => (s.w === w && s.h === h && s.notch === notch ? s : { w, h, notch }))
    }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  return { ref, ...size }
}

/** 둥근 모서리 화살표. 첫 칸은 왼쪽이 평평하다. 뾰족한 끝·들어간 곳까지 모두 둥글게. */
function chevronPath(w: number, h: number, first: boolean, notch: number) {
  const pts: [number, number][] = first
    ? [[0, 0], [w - notch, 0], [w, h / 2], [w - notch, h], [0, h]]
    : [[0, 0], [w - notch, 0], [w, h / 2], [w - notch, h], [0, h], [notch, h / 2]]
  return roundedPolygon(pts, Math.min(14, h / 8))
}

function roundedPolygon(pts: [number, number][], r: number) {
  const n = pts.length
  let d = ''
  for (let i = 0; i < n; i++) {
    const [px, py] = pts[(i - 1 + n) % n]
    const [cx, cy] = pts[i]
    const [nx, ny] = pts[(i + 1) % n]
    const toPrev = Math.hypot(px - cx, py - cy)
    const toNext = Math.hypot(nx - cx, ny - cy)
    const k = Math.min(r, toPrev / 2, toNext / 2)
    const ax = cx + ((px - cx) / toPrev) * k
    const ay = cy + ((py - cy) / toPrev) * k
    const bx = cx + ((nx - cx) / toNext) * k
    const by = cy + ((ny - cy) / toNext) * k
    d += `${i === 0 ? 'M' : 'L'}${ax.toFixed(1)},${ay.toFixed(1)} Q${cx},${cy} ${bx.toFixed(1)},${by.toFixed(1)} `
  }
  return d + 'Z'
}
