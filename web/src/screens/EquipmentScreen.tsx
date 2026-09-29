import { AlertTriangle, Check, RotateCw } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import InfoTip from '../components/InfoTip'
import JarvisRing from '../components/JarvisRing'
import { useCheckStream, type CheckItem } from '../checks'
import styles from './EquipmentScreen.module.css'

/** 전원 허브: 다른 장비에 전원을 주므로, 허브가 실패하면 나머지는 보류하고 허브부터 해결한다 (서버와 같은 규칙) */
const HUB = 'switch'
const WAITING_FOR_HUB = '전원 허브가 연결되면 확인합니다'

// 배치 비율 (그래프 한 변 = 100). 가운데 원 지름, 장비 원이 놓이는 반지름, 장비 원 기본 지름
const CENTER = 28
const ORBIT = 36
const NODE = 17

/**
 * 2단계 장비 연결: 1단계의 원형 표시가 작아져 가운데에 남고, 장비 원들이 방사형으로 둘러싼다.
 * 연결에 성공한 장비는 가운데와 선으로 이어진다. 모든 장비가 필수.
 * [임시] 지금은 서버 설정 Equipment:Simulate로 모두 연결된 것으로 간주한다 (실제 장비 없이 개발).
 */
export default function EquipmentScreen({ onContinue }: { onContinue: (items: CheckItem[]) => void }) {
  const [plan, setPlan] = useState<CheckItem[] | null>(null)

  useEffect(() => {
    fetch('/api/equipment/plan')
      .then((r) => (r.ok ? r.json() : []))
      .then(setPlan, () => setPlan([]))
  }, [])

  if (plan === null) return <main className={styles.stage} />
  return <EquipmentGraph plan={plan} onContinue={onContinue} />
}

function EquipmentGraph({ plan, onContinue }: { plan: CheckItem[]; onContinue: (items: CheckItem[]) => void }) {
  const { items, done, allPass, failed, restart, recheckOne, patch } = useCheckStream('/api/equipment/connect', plan)
  const [picked, setPicked] = useState<string | null>(null)
  const ringBox = useRef<HTMLDivElement>(null)
  const [shrinkFrom, setShrinkFrom] = useState(1)

  // 1단계의 원(clamp(240px, 34vh, 340px))에서 지금 크기로 줄어드는 것처럼 시작한다
  useLayoutEffect(() => {
    const w = ringBox.current?.offsetWidth
    if (!w) return
    const engine = Math.min(340, Math.max(240, window.innerHeight * 0.34))
    setShrinkFrom(engine / w)
  }, [])

  // 모두 연결되면 선이 다 이어진 모습을 잠깐 보여 주고 바로 다음 단계로 (누를 필요 없는 "다음"은 두지 않음)
  useEffect(() => {
    if (!allPass) return
    const t = setTimeout(() => onContinue(items), 1600)
    return () => clearTimeout(t)
  }, [allPass, items, onContinue])

  const state = allPass ? 'done' : done && failed ? 'failed' : 'working'
  const missing = items.filter((i) => i.status === 'Fail').length
  const hubFailed = items.some((i) => i.id === HUB && i.status === 'Fail')
  const centerLine =
    state === 'done'
      ? '모든 장비가 연결되었습니다'
      : state === 'failed'
        ? hubFailed
          ? '전원 허브를 먼저 연결해 주세요'
          : `연결되지 않은 장비가 ${missing}개 있습니다`
        : '장비 연결 중입니다'

  // 아래 상태 줄에 보여 줄 장비: 고른 장비 → 첫 실패 장비
  const selected =
    items.find((i) => i.id === picked && i.status !== 'Pass') ?? items.find((i) => i.status === 'Fail')

  const retry = (item: CheckItem) => {
    setPicked(null)
    // 허브는 처음부터: 허브를 다시 연결하고, 보류된 뒤 장비도 이어서 연결한다
    if (item.id === HUB) restart()
    else void recheckOne(item.id, `/api/equipment/connect/${item.id}`)
  }

  return (
    <main className={styles.stage}>
      <div className={styles.graph}>
        {/* 연결 선: 가운데 원 가장자리 → 장비 원 가장자리. 연결에 성공하면 그려진다 */}
        <svg className={styles.links} viewBox="0 0 100 100" aria-hidden="true">
          {items.map((item, i) => {
            const n = layout(item.id, i, items.length)
            const ux = Math.cos(n.angle)
            const uy = Math.sin(n.angle)
            return (
              <line
                key={item.id}
                className={styles.link}
                data-on={item.status === 'Pass'}
                x1={50 + ux * (CENTER / 2 + 1.5)}
                y1={50 + uy * (CENTER / 2 + 1.5)}
                x2={50 + ux * (n.radius - n.size / 2 - 1)}
                y2={50 + uy * (n.radius - n.size / 2 - 1)}
                pathLength={1}
              />
            )
          })}
        </svg>

        <div ref={ringBox} className={styles.center} style={{ '--shrink-from': shrinkFrom } as CSSProperties}>
          <JarvisRing state={state} className={styles.ring}>
            {centerLine}
          </JarvisRing>
        </div>

        {items.map((item, i) => {
          const n = layout(item.id, i, items.length)
          const x = 50 + Math.cos(n.angle) * n.radius
          const y = 50 + Math.sin(n.angle) * n.radius
          return (
            <button
              key={item.id}
              type="button"
              className={styles.node}
              data-status={item.status}
              data-selected={selected?.id === item.id}
              style={{ left: `${x - n.size / 2}%`, top: `${y - n.size / 2}%`, width: `${n.size}%`, '--i': i } as CSSProperties}
              onClick={() => setPicked(item.id)}
              aria-label={`${item.title} ${item.term ?? ''}: ${statusText(item)}`}
            >
              <span className={styles.kind}>{item.title}</span>
              {item.term && <span className={styles.name}>{item.term}</span>}
              {/* 상태 아이콘 자리는 항상 둔다 (글자가 움직이지 않게) */}
              <span className={styles.mark} aria-hidden="true">
                {item.status === 'Pass' && <Check strokeWidth={2.5} />}
                {item.status === 'Fail' && <AlertTriangle strokeWidth={2} />}
              </span>
            </button>
          )
        })}
      </div>

      {/* 아래 상태 줄: 연결 안 된 장비의 상태·해결 방법·다시 시도. 자리는 항상 확보 */}
      <div className={styles.status} data-shown={!!selected && done}>
        {selected && (
          <>
            <span className={styles.statusName}>
              {selected.title}
              {selected.term && <small>{selected.term}</small>}
            </span>
            <span className={styles.statusText} data-status={selected.status}>
              {statusText(selected)}
            </span>
            {selected.diagnosis?.fix && <InfoTip label="해결 방법" text={selected.diagnosis.fix} />}
            {selected.status === 'Fail' && (
              <button type="button" className={styles.retry} onClick={() => retry(selected)}>
                <RotateCw strokeWidth={2} aria-hidden="true" />
                다시 연결
              </button>
            )}
          </>
        )}
      </div>

      <TempFailButtons items={items} done={done} patch={patch} />
    </main>
  )
}

function statusText(item: CheckItem) {
  switch (item.status) {
    case 'Pass':
      return '연결되어 있습니다'
    case 'Fail':
      return '연결되어 있지 않습니다'
    case 'Running':
      return '연결하는 중입니다'
    case 'Skipped':
      return '전원 허브를 먼저 연결해 주세요'
    default:
      return '연결 대기 중입니다'
  }
}

/**
 * 장비 원의 자리와 크기. 원은 가운데를 고르게 둘러싸되, 크기는 장비마다 조금씩 다르게(±12%)
 * 해서 자연스럽게 보이게 한다. 값은 장비 id로 정해지므로 다시 열어도 같은 모양이다.
 */
function layout(id: string, index: number, count: number) {
  const r1 = seeded(id, 1)
  const r2 = seeded(id, 2)
  const size = NODE * (0.88 + r1 * 0.24)
  const radius = ORBIT * (0.97 + r2 * 0.06)
  const angle = -Math.PI / 2 + (index / Math.max(count, 1)) * Math.PI * 2
  return { size, radius, angle }
}

/** id로 정해지는 0~1 사이 값 (매번 같은 값) */
function seeded(id: string, salt: number) {
  let h = 2166136261 ^ salt
  for (let i = 0; i < id.length; i++) h = Math.imul(h ^ id.charCodeAt(i), 16777619)
  return ((h >>> 0) % 1000) / 1000
}

/**
 * [임시] 화면 설계용: 장비마다 "연결 실패로 만들기" 버튼.
 * 실패 결과는 서버가 실제 실패와 같은 모양으로 만든다 (Equipment:Simulate일 때만).
 * 되돌리기는 "다시 연결" — 시뮬레이션에서는 다시 연결에 성공한다.
 */
function TempFailButtons({
  items,
  done,
  patch,
}: {
  items: CheckItem[]
  done: boolean
  patch: (fn: (prev: CheckItem[]) => CheckItem[]) => void
}) {
  const fail = async (id: string) => {
    const res = await fetch(`/api/equipment/connect/${id}?simulateFail=true`)
    if (!res.ok) return
    const result = (await res.json()) as CheckItem
    patch((prev) =>
      prev.map((i) =>
        i.id === id
          ? result
          : id === HUB
            ? { ...i, status: 'Skipped' as const, message: WAITING_FOR_HUB, diagnosis: null }
            : i,
      ),
    )
  }

  const connected = items.filter((i) => i.status === 'Pass')
  if (!done || connected.length === 0) return null
  return (
    <div className={styles.temp}>
      <span>연결 실패로 만들기 (임시)</span>
      {connected.map((i) => (
        <button key={i.id} type="button" onClick={() => fail(i.id)}>
          {i.title}
        </button>
      ))}
    </div>
  )
}
