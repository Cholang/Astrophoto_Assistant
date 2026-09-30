import { Check } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import styles from './JarvisRing.module.css'

export type RingState = 'working' | 'done' | 'failed'

/** 글자 바꿈: 흐려졌다가 새 글자로 밝아진다. 한 문장은 적어도 이만큼 보여 준다 (빨리 지나가는 단계가 번쩍이지 않게) */
const FADE_MS = 240
const MIN_SHOW_MS = 800

/**
 * 가운데 원형 진행 표시 (여러 겹의 호가 서로 다른 속도로 돈다).
 * 사용자가 고를 것이 없는 진행 단계에 쓴다 (DESIGN.md 3장). 크기는 감싸는 요소가 정한다.
 * 완료: 회전을 멈추고 바깥 원을 닫아 초록 + 체크. 실패: 회전을 멈추고 오류 색.
 * 상태·글자가 바뀔 때는 뚝 끊기지 않게 천천히 바뀐다.
 */
export default function JarvisRing({ state, children, className }: { state: RingState; children: ReactNode; className?: string }) {
  // 완료 문장은 기다리지 않고 바로 (완료 순간과 글자가 어긋나 보이지 않게)
  const { shown, visible } = useSmoothContent(children, state === 'done')
  return (
    <div className={[styles.ring, className].filter(Boolean).join(' ')} data-state={state} role="status" aria-live="polite">
      <svg className={styles.arcs} viewBox="0 0 200 200" aria-hidden="true">
        <circle className={styles.track} cx="100" cy="100" r="92" />
        <circle className={styles.outer} cx="100" cy="100" r="92" pathLength="100" />
        <circle className={styles.middle} cx="100" cy="100" r="80" pathLength="100" />
        <circle className={styles.inner} cx="100" cy="100" r="70" pathLength="100" />
      </svg>
      <div className={styles.center}>
        {/* 아이콘은 글자 위에 겹쳐 놓고 상태에 따라 보이기만 바꾼다 (글자는 항상 세로 가운데, 움직이지 않게) */}
        <span className={styles.icon}>
          <Check strokeWidth={2.5} aria-hidden="true" data-shown={state === 'done'} />
        </span>
        <div className={styles.line} data-visible={visible}>
          {shown}
        </div>
      </div>
    </div>
  )
}

/**
 * 글자를 부드럽게 바꾼다: 새 글자가 오면 지금 글자를 흐리게 → 바꾸고 → 밝게.
 * 앞 글자가 MIN_SHOW_MS보다 짧게 보였으면 그만큼 기다렸다가 바꾼다. 그사이 또 바뀌면 마지막 것만 보인다.
 * immediate: 기다리지도 흐려지지도 않고 바로 바꾼다 (완료 문장)
 */
function useSmoothContent(content: ReactNode, immediate = false) {
  const [shown, setShown] = useState(content)
  const [visible, setVisible] = useState(true)
  const shownAt = useRef(0)
  const latest = useRef(content)
  latest.current = content

  // 문자열이면 글자로 비교 (같은 글자를 다시 그릴 때는 바꾸지 않는다)
  const key = typeof content === 'string' ? content : null
  const shownKey = typeof shown === 'string' ? shown : null

  useEffect(() => {
    if (shownAt.current === 0) shownAt.current = performance.now()
  }, [])

  useEffect(() => {
    if (key !== null && key === shownKey) return
    if (key === null) {
      setShown(content)
      return
    }
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    if (reduce || immediate) {
      setVisible(true)
      setShown(latest.current)
      shownAt.current = performance.now()
      return
    }
    const wait = Math.max(0, MIN_SHOW_MS - (performance.now() - shownAt.current))
    const t1 = setTimeout(() => setVisible(false), wait)
    const t2 = setTimeout(() => {
      setShown(latest.current)
      setVisible(true)
      shownAt.current = performance.now()
    }, wait + FADE_MS)
    return () => {
      clearTimeout(t1)
      clearTimeout(t2)
    }
    // content는 key로 대신 본다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, shownKey, immediate])

  return { shown, visible }
}
