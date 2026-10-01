import { Check } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import styles from './PreflightScreen.module.css'

interface Item {
  title: string
  question: string
  /** [임시] 제안 항목 표시 */
  tag?: string
  pic: ReactNode
}

// 그림: 64×64 선 그림. 강조할 부분만 .acc (강조색)
const ITEMS: Item[] = [
  {
    title: '적도의 시작 위치',
    question: '망원경은 북극성 쪽, 균형추는 아래를 향하게 두었나요?',
    pic: (
      <>
        <path className={styles.acc} d="M50 8l1.6 3.4 3.7.4-2.8 2.5.8 3.7-3.3-1.9-3.3 1.9.8-3.7-2.8-2.5 3.7-.4z" />
        <path d="M22 58l10-22 10 22" />
        <path d="M32 36v-6" />
        <path d="M26 34l20-20" />
        <rect x="30" y="12" width="20" height="8" rx="3" transform="rotate(-45 40 16)" />
        <path d="M32 30v12" />
        <circle cx="32" cy="46" r="4" />
      </>
    ),
  },
  {
    title: '망원경 캡',
    question: '망원경 앞 뚜껑과 가이드 망원경 뚜껑을 모두 열었나요?',
    pic: (
      <>
        <rect x="10" y="24" width="30" height="16" rx="3" />
        <path d="M40 22v20" />
        <rect x="48" y="20" width="6" height="24" rx="2.5" />
        <path className={styles.acc} d="M44 32h-2" />
        <path className={styles.acc} d="M58 14l-4 4" />
        <path className={styles.acc} d="M58 50l-4-4" />
      </>
    ),
  },
  {
    title: '케이블 정리',
    question: '적도의가 한 바퀴 돌아도 케이블이 당기거나 걸리지 않나요?',
    pic: (
      <>
        <path d="M8 44c8 0 8-14 16-14s8 14 16 14 8-14 16-14" />
        <rect className={styles.acc} x="20" y="26" width="8" height="8" rx="2" />
        <rect className={styles.acc} x="36" y="40" width="8" height="8" rx="2" />
        <path d="M8 44v8" />
        <path d="M56 30v-8" />
      </>
    ),
  },
  {
    title: '망원경 밸런스',
    question: '클러치를 풀었을 때 망원경이 어느 쪽으로도 쏠리지 않나요?',
    pic: (
      <>
        <path d="M32 40l-7 12h14z" />
        <path d="M8 38h48" />
        <rect x="10" y="28" width="12" height="10" rx="2" />
        <circle cx="48" cy="32" r="6" />
        <path className={styles.acc} d="M32 14v8" />
        <path className={styles.acc} d="M28 18h8" />
      </>
    ),
  },
  {
    title: '삼각대 수평',
    question: '삼각대의 수평계 기포가 가운데에 있나요?',
    pic: (
      <>
        <rect x="8" y="24" width="48" height="16" rx="8" />
        <path d="M26 24v16" />
        <path d="M38 24v16" />
        <circle className={styles.acc} cx="32" cy="32" r="3.5" />
        <path d="M20 48l-6 10" />
        <path d="M44 48l6 10" />
        <path d="M32 48v10" />
      </>
    ),
  },
  {
    title: '이슬 방지 열선',
    question: '망원경 끝과 가이드 망원경에 열선을 감고 허브에 연결했나요?',
    tag: '제안 항목',
    pic: (
      <>
        <rect x="8" y="24" width="40" height="16" rx="3" />
        <path className={styles.acc} d="M34 22v20M40 22v20" />
        <path d="M37 42c0 6 6 6 6 12" />
        <path d="M52 16c2 2-2 4 0 6M58 16c2 2-2 4 0 6" />
      </>
    ),
  },
]

// 동작 시간 (DESIGN.md 3장 "사용자가 직접 확인하는 체크리스트")
const SINK_MS = 260 // "모두 확인 완료" 버튼이 가라앉은 뒤 타일이 사라지기 시작
const LEAVE_MS = 340 // 타일이 투명해지는 시간 (CSS와 같게)
const SKIP_FIRST_MS = 320 // "모두 확인 완료" 후 첫 타일이 확인됨으로 바뀌기까지
const SKIP_STEP_MS = 220 // 남은 타일 사이 간격

/**
 * 출발 전 점검: AA가 스스로 알 수 없는 것만 사용자에게 묻는다.
 * 사용자가 직접 체크하는 화면이라 6개를 다 채우면 "다음" 없이 자동 진행 (DESIGN.md 1장).
 */
export default function PreflightScreen({ onContinue }: { onContinue: () => void }) {
  const [on, setOn] = useState<boolean[]>(() => ITEMS.map(() => false))
  const [locked, setLocked] = useState(false)
  const [sunk, setSunk] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const timers = useRef<number[]>([])
  const finished = useRef(false)

  const later = useCallback((fn: () => void, ms: number) => {
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    timers.current.push(window.setTimeout(fn, reduce ? 0 : ms))
  }, [])

  useEffect(() => () => timers.current.forEach(clearTimeout), [])

  const count = on.filter(Boolean).length

  // 6개가 모두 확인되는 순간: 타일이 한꺼번에 투명해지고 다음 단계로 (하나씩 체크·"모두 확인 완료" 공통)
  useEffect(() => {
    if (count < ITEMS.length || finished.current) return
    finished.current = true
    setLocked(true)
    setSunk(true)
    later(() => setLeaving(true), SINK_MS)
    later(onContinue, SINK_MS + LEAVE_MS)
  }, [count, later, onContinue])

  const toggle = (i: number) => {
    if (locked) return
    setOn((list) => list.map((v, k) => (k === i ? !v : v)))
  }

  // "모두 확인 완료": 버튼이 가라앉아 사라지고, 남은 타일이 왼쪽 위부터 차례로 확인됨으로 바뀐다
  const skip = () => {
    if (locked) return
    setLocked(true)
    setSunk(true)
    on.map((v, i) => (v ? -1 : i))
      .filter((i) => i >= 0)
      .forEach((i, k) => later(() => setOn((list) => list.map((v, j) => (j === i ? true : v))), SKIP_FIRST_MS + k * SKIP_STEP_MS))
  }

  return (
    <main className={styles.stage} data-leaving={leaving}>
      <div className={styles.intro}>
        <h1>촬영 시작 전에 장비를 한 번 둘러봐 주세요</h1>
      </div>

      {/* 확인 개수: 오른쪽 위 타일의 모서리 바로 위, 타일 바깥 */}
      <div className={styles.head}>
        <span className={styles.count} aria-live="polite">
          <b>{count}</b>/{ITEMS.length}
        </span>
      </div>

      <div className={styles.tiles}>
        {ITEMS.map((item, i) => (
          <button key={item.title} type="button" className={styles.tile} data-on={on[i]} aria-pressed={on[i]} onClick={() => toggle(i)}>
            {/* 그림 카드: 확인하면 뒤집히며 뒷면(그림 + V 표시)이 보이고, 해제하면 반대로 뒤집힌다 */}
            <span className={styles.pic} aria-hidden="true">
              <span className={styles.flip}>
                <span className={styles.face}>
                  <svg viewBox="0 0 64 64">{item.pic}</svg>
                </span>
                <span className={`${styles.face} ${styles.back}`}>
                  <svg viewBox="0 0 64 64">{item.pic}</svg>
                  <span className={styles.checkMark}>
                    <Check strokeWidth={3} />
                  </span>
                </span>
              </span>
            </span>
            <span className={styles.text}>
              <span className={styles.title}>{item.title}</span>
              <span className={styles.question}>{item.question}</span>
              {item.tag && <span className={styles.tag}>{item.tag}</span>}
            </span>
          </button>
        ))}
      </div>

      {/* 하단: 버튼 하나. 영역 높이는 고정이라 버튼이 사라져도 타일은 움직이지 않는다 */}
      <div className={styles.foot}>
        <button type="button" className={styles.skip} data-sunk={sunk} onClick={skip} disabled={locked}>
          모두 확인 완료
        </button>
      </div>
    </main>
  )
}
