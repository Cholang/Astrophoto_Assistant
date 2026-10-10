import { Check } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import Button from '../components/Button'
import styles from './PreflightScreen.module.css'

interface Item {
  /** 포커서 0점 카드 — 지난번 포커서를 0에 두고 끝냈으면 묻지 않는다 */
  focuser?: boolean
  title: string
  question: string
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
  // 포커서 0점 (2026-10-09 사용자 결정 — 이슬 방지 열선 자리. 열선은 전원 허브 자동에 맡김):
  // 포커서를 다시 달면 기어가 맞물리며 노브가 조금 돌아가므로, 노브를 0에 맞춰 달고 AA가 장비 준비의 초점 작업에서 0점을 잡는다.
  // 지난번 아이라가 포커서를 0에 두고 끝냈고 지금도 0이면 이 카드는 빠진다 (2026-10-09 — 서버 FocuserPark)
  {
    focuser: true,
    title: '포커서 0점',
    question: '포커서를 달 때 초점 노브를 끝까지 넣어 0에 맞췄나요?',
    pic: (
      <>
        <rect x="6" y="24" width="30" height="16" rx="3" />
        <rect x="36" y="27" width="12" height="10" rx="1.5" />
        <circle cx="42" cy="46" r="5" />
        <path d="M42 41v-4" />
        <path className={styles.acc} d="M58 32h-8" />
        <path className={styles.acc} d="M53 28l-4 4 4 4" />
        <path className={styles.acc} d="M54 14v8" />
      </>
    ),
  },
]

// 동작 시간 (DESIGN.md 3장 "사용자가 직접 확인하는 체크리스트")
const CARD_MS = 520 // 카드가 떠오르며 사라지는 시간 (CSS와 같게)
const SINK_MS = 260 // "모두 확인 완료" 버튼이 가라앉은 뒤 첫 카드가 넘어가기까지
const SKIP_STEP_MS = 220 // 남은 카드가 차례로 넘어가는 간격
const LEAVE_MS = 340 // 마지막 카드 뒤 화면이 투명해지는 시간 (CSS와 같게)
/** 덱 뒤로 보이는 카드 수 (그보다 뒤는 숨김) */
const DEPTH = 5

/**
 * 출발 전 점검: AA가 스스로 알 수 없는 것만 사용자에게 묻는다 (2026-10-09 카드덱으로 바꿈).
 * 가운데 카드덱의 맨 위 카드를 누르면 V가 붙고 눌렸다가 떠오르며 사라져 다음 카드가 보인다. 오른쪽은 맨 위 카드의 설명.
 * 사용자가 직접 체크하는 화면이라 6장을 다 넘기면 "다음" 없이 자동 진행 (DESIGN.md 1장).
 */
export default function PreflightScreen({ onContinue }: { onContinue: () => void }) {
  /** 넘긴 카드 수 = 맨 위 카드의 번호 */
  const [done, setDone] = useState(0)
  const [locked, setLocked] = useState(false)
  const [sunk, setSunk] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const timers = useRef<number[]>([])
  const finished = useRef(false)
  // 카드 목록: 포커서를 0에 두고 끝냈는지 물어본 뒤 정한다 (그 전에는 덱을 보이지 않음 — 장 수가 바뀌어 보이지 않게). 답이 늦으면 6장
  const [items, setItems] = useState<Item[] | null>(null)
  useEffect(() => {
    const decide = (list: Item[]) => setItems((prev) => prev ?? list)
    const slow = window.setTimeout(() => decide(ITEMS), 1500)
    fetch('/api/focuser/parked')
      .then((r) => (r.ok ? (r.json() as Promise<{ ready: boolean }>) : null))
      .then((b) => decide(b?.ready ? ITEMS.filter((i) => !i.focuser) : ITEMS))
      .catch(() => decide(ITEMS))
      .finally(() => clearTimeout(slow))
    return () => clearTimeout(slow)
  }, [])
  const list = items ?? ITEMS

  const later = useCallback((fn: () => void, ms: number) => {
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    timers.current.push(window.setTimeout(fn, reduce ? 0 : ms))
  }, [])

  useEffect(() => () => timers.current.forEach(clearTimeout), [])

  // 6장을 다 넘기면: 마지막 카드가 사라진 뒤 화면이 투명해지고 다음 단계로 (한 장씩·"모두 확인 완료" 공통)
  useEffect(() => {
    if (!items || done < items.length || finished.current) return
    finished.current = true
    setLocked(true)
    setSunk(true)
    later(() => setLeaving(true), CARD_MS)
    later(onContinue, CARD_MS + LEAVE_MS)
  }, [done, items, later, onContinue])

  const check = () => {
    if (locked || !items) return
    setDone((d) => Math.min(d + 1, list.length))
  }

  // "모두 확인 완료": 버튼이 가라앉아 사라지고, 남은 카드가 차례로 넘어간다
  const skip = () => {
    if (locked || !items) return
    setLocked(true)
    setSunk(true)
    for (let k = 0; k < list.length - done; k++) later(() => setDone((d) => Math.min(d + 1, list.length)), SINK_MS + k * SKIP_STEP_MS)
  }

  const top = Math.min(done, list.length - 1)
  const current = list[top]

  return (
    <main className={styles.stage} data-leaving={leaving} data-ready={items !== null}>
      <div className={styles.intro}>
        <h1>촬영 시작 전에 장비를 한 번 둘러봐 주세요</h1>
      </div>

      <div className={styles.table}>
        {/* 카드덱: 뒤 카드일수록 오른쪽 아래로 비켜 쌓인다. 넘긴 카드는 V를 달고 떠오르며 사라진다 */}
        <div className={styles.deck}>
          {list.map((item, i) => {
            const depth = i - done
            const state = depth < 0 ? 'gone' : depth === 0 ? 'top' : 'under'
            return (
              <button
                key={item.title}
                type="button"
                className={styles.card}
                data-state={state}
                style={{ '--depth': Math.max(depth, 0), zIndex: list.length - i, visibility: depth > DEPTH ? 'hidden' : undefined } as CSSProperties}
                tabIndex={state === 'top' ? 0 : -1}
                aria-hidden={state !== 'top'}
                aria-label={state === 'top' ? `${item.title}: ${item.question} 확인했으면 누르세요` : undefined}
                disabled={state !== 'top' || locked}
                onClick={check}
              >
                <svg className={styles.pic} viewBox="0 0 64 64" aria-hidden="true">
                  {item.pic}
                </svg>
                <span className={styles.checkMark} aria-hidden="true">
                  <Check strokeWidth={3} />
                </span>
              </button>
            )
          })}
        </div>

        {/* 설명: 맨 위 카드의 이름·질문. 카드가 넘어가면 글만 바뀐다 (자리 고정) */}
        <div className={styles.about} aria-live="polite">
          <span className={styles.count}>
            <b>{Math.min(done + 1, list.length)}</b>/{list.length}
          </span>
          <div key={current.title} className={styles.words}>
            <h2 className={styles.title}>{current.title}</h2>
            <p className={styles.question}>{current.question}</p>
            <p className={styles.hint}>확인했으면 카드를 누르세요</p>
          </div>
        </div>
      </div>

      {/* 하단: 버튼 하나. 영역 높이는 고정이라 버튼이 사라져도 덱은 움직이지 않는다 */}
      <div className={styles.foot}>
        <div className={styles.sink} data-sunk={sunk}>
          <Button size="lg" className={styles.skip} onClick={skip} disabled={locked}>
          모두 확인 완료
          </Button>
        </div>
      </div>
    </main>
  )
}
