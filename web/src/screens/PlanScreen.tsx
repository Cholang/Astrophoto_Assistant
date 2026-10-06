import { ArrowRight } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import Button from '../components/Button'
import NightChart from '../components/NightChart'
import { useRailExtra } from '../components/StepRail'
import { PRODUCT } from '../product'
import { sendChat, type Message, type Plan, type PlanState } from '../plan'
import styles from './PlanScreen.module.css'

const CELL_NAMES = {
  target: '대상',
  framing: '구도',
  settings: '촬영 설정',
  after: '끝난 뒤',
} as const
type Field = keyof typeof CELL_NAMES

/**
 * 촬영 계획 (DESIGN.md 3장 "촬영 계획 화면"): 왼쪽 1/3 AA와의 대화, 오른쪽 위 오늘 밤 조건, 아래 계획 네 칸 + 계획 카드 줄.
 * 계획 칸은 AI가 도구로 채운다. 숫자는 모두 서버 계산 값이다.
 * 이 화면에서 적도의는 움직이지 않는다.
 */
export default function PlanScreen({ onContinue }: { onContinue: () => void }) {
  const [state, setState] = useState<PlanState | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoadError(null)
    fetch('/api/plan/state')
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((s: PlanState) => (s.error ? setLoadError(s.error) : setState(s)))
      .catch(() => setLoadError(`${PRODUCT.name} 내부 서버에서 오늘 밤 정보를 받지 못했습니다.`))
  }, [])

  useEffect(load, [load])

  if (loadError)
    return (
      <main className={styles.problem}>
        <p>{loadError}</p>
        <Button onClick={load}>다시 시도</Button>
      </main>
    )
  if (!state) return <main className={styles.plan} aria-busy="true" />
  return <PlanBody initial={state} onContinue={onContinue} />
}

/** 대상 단계의 작업 (진행 표시). 계획 화면에서는 계획이 지금 작업이고 나머지는 대상 묶음(서버)이 이어서 한다 */
const TARGET_ITEMS = [
  ['plan', '계획'],
  ['slew', '이동'],
  ['center', '센터링'],
  ['focuscheck', '초점 확인'],
  ['guiding', '가이딩'],
  ['test', '시험 사진'],
] as const

function PlanBody({ initial, onContinue }: { initial: PlanState; onContinue: () => void }) {
  useRailExtra({ stage: '대상', items: TARGET_ITEMS.map(([id, label], i) => ({ id, label, state: i === 0 ? 'now' : 'todo' })) })
  const [messages, setMessages] = useState<Message[]>(initial.messages)
  const [plan, setPlan] = useState<Plan>(initial.plan)
  const [chart, setChart] = useState(initial.chart)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const listRef = useRef<HTMLDivElement>(null)

  // 새 글이 오면 맨 아래로
  useEffect(() => {
    const el = listRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages, busy])

  const send = useCallback(
    async (text: string, fromChoice = false) => {
      text = text.trim()
      if (!text || busy) return
      setBusy(true)
      setError(null)
      setDraft('')
      setMessages((list) => {
        const next = [...list]
        // 선택지를 눌렀으면 그 메시지에 고른 것으로 남긴다
        if (fromChoice) {
          const i = next.findLastIndex((m) => m.role === 'assistant' && m.choices?.includes(text))
          if (i >= 0) next[i] = { ...next[i], picked: text }
        }
        return [...next, { role: 'user', text }, { role: 'assistant', text: '' }]
      })
      let failed: string | null = null
      try {
        await sendChat(text, (e) => {
          if (e.type === 'text')
            setMessages((list) => {
              const last = list[list.length - 1]
              return [...list.slice(0, -1), { ...last, text: last.text + e.text }]
            })
          else if (e.type === 'choices')
            setMessages((list) => {
              const last = list[list.length - 1]
              return [...list.slice(0, -1), { ...last, choices: e.choices }]
            })
          else if (e.type === 'plan') setPlan(e.plan)
          else if (e.type === 'notice')
            // 안내 줄은 지금 쓰는 답(마지막 빈 말풍선) 앞에 넣는다
            setMessages((list) => [...list.slice(0, -1), { role: 'notice', text: e.message }, list[list.length - 1]])
          else if (e.type === 'error') failed = e.message
        })
      } catch {
        failed = `${PRODUCT.name} 내부 서버와 연결이 끊겼습니다. 다시 보내 주세요.`
      }
      if (failed) {
        // 실패한 말은 대화에서 빼고 입력창에 되돌려 둔다 (서버도 기록에서 뺐다)
        setMessages((list) => {
          const trimmed = list.slice(0, -2)
          return fromChoice
            ? trimmed.map((m, i) => (i === trimmed.length - 1 && m.picked === text ? { ...m, picked: undefined } : m))
            : trimmed
        })
        if (!fromChoice) setDraft(text)
        setError(failed)
      }
      // 그래프: 대상 곡선·촬영 시간대가 바뀌었을 수 있다
      fetch('/api/plan/chart')
        .then((r) => (r.ok ? r.json() : null))
        .then((c) => c && setChart(c))
        .catch(() => {})
      setBusy(false)
    },
    [busy],
  )

  // "이 대상으로 이동": 서버가 계획을 실행 값으로 확정하고 대상 묶음(이동부터)으로 넘어간다. 실행할 수 없는 계획이면(관측 가능한 시간 없음 등) 이유를 오류 줄에
  const [starting, setStarting] = useState(false)
  const start = async () => {
    if (starting) return
    setStarting(true)
    setError(null)
    try {
      const res = await fetch('/api/plan/confirm', { method: 'POST' })
      if (res.ok) return onContinue()
      const body = (await res.json().catch(() => null)) as { error?: string } | null
      setError(body?.error ?? '계획을 확정하지 못했습니다. 다시 눌러 주세요.')
    } catch {
      setError(`${PRODUCT.name} 내부 서버와 연결이 끊겼습니다. 다시 눌러 주세요.`)
    }
    setStarting(false)
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    void send(draft)
  }

  const lastAssistant = messages.findLastIndex((m) => m.role === 'assistant')

  return (
    <main className={styles.plan}>
      <aside className={styles.chat} aria-label={`${PRODUCT.name}와 대화`}>
        <div className={styles.messages} ref={listRef} aria-live="polite">
          {messages.map((m, i) =>
            m.role === 'notice' ? (
              <p key={i} className={styles.notice}>
                {m.text}
              </p>
            ) : (
              <div key={i} className={styles.turn}>
                <div className={styles.message} data-role={m.role}>
                  {m.role === 'assistant' && <span className={styles.who}>{PRODUCT.name}</span>}
                  {m.text || (busy && i === messages.length - 1 ? <span className={styles.typing} aria-label="답을 쓰는 중" /> : null)}
                </div>
                {m.role === 'assistant' && m.choices && m.choices.length > 0 && (
                  <div className={styles.choices}>
                    {m.choices.map((c) => (
                      <button
                        key={c}
                        type="button"
                        className={styles.choice}
                        data-picked={m.picked === c}
                        disabled={busy || i !== lastAssistant || !!m.picked}
                        onClick={() => void send(c, true)}
                      >
                        {c}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ),
          )}
        </div>
        {/* 오류 줄: 자리는 항상 확보 */}
        <p className={styles.error} data-shown={!!error} role="alert">
          {error}
        </p>
        <form className={styles.composer} onSubmit={submit}>
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder={`${PRODUCT.name}에게 말하기`}
            aria-label={`${PRODUCT.name}에게 말하기`}
            maxLength={300}
          />
          <button type="submit" className={styles.send} disabled={busy || !draft.trim()} aria-label="보내기">
            <ArrowRight strokeWidth={2} />
          </button>
        </form>
      </aside>

      <div className={styles.right}>
        <section className={styles.night}>
          <div className={styles.nightHead}>
            <b>오늘 밤</b>
            <span>어두운 시간 {chart.dark.label}</span>
            <span>달 {chart.moon.illumination}%</span>
            <span>{initial.cloudsAvailable ? '구름 예보 받음' : '구름 예보 없음'}</span>
            <span className={styles.legend} data-shown={!!chart.shooting}>
              <span className={styles.swatch} />
              촬영 시간
            </span>
          </div>
          <NightChart chart={chart} />
        </section>

        <div className={styles.lower}>
          <div className={styles.cells}>
            {(Object.keys(CELL_NAMES) as Field[]).map((f) => (
              <PlanCell key={f} field={f} plan={plan} disabled={busy} onEdit={() => void send(`${CELL_NAMES[f]} 고치기`)} />
            ))}
          </div>
          {/* 계획 카드 줄: 자리만 확보해 두었다가 네 칸이 모두 정해지면 나타난다 */}
          <div className={styles.card} data-shown={plan.complete}>
            <div className={styles.summary}>
              {summary(plan)}
              <small>{summarySub(plan)}</small>
            </div>
            <Button variant="primary" onClick={() => void start()} disabled={!plan.complete || busy || starting} tabIndex={plan.complete ? 0 : -1}>
              이 대상으로 이동
            </Button>
          </div>
        </div>
      </div>
    </main>
  )
}

function PlanCell({ field, plan, disabled, onEdit }: { field: Field; plan: Plan; disabled: boolean; onEdit: () => void }) {
  const cell = plan[field]
  const stateText = cell.state === 'set' ? '정해짐' : cell.state === 'asking' ? '묻는 중' : ''
  let body: ReactNode = <p className={styles.empty}>대화를 진행하면 채워집니다</p>

  if (field === 'target' && plan.target.value) {
    const t = plan.target.value
    body = (
      <div className={styles.cellBody}>
        <div>
          <div className={styles.big}>
            {t.name} {t.koreanName ?? t.commonName ?? ''}
          </div>
          <div className={styles.sub}>
            {t.constellation} {t.type}
          </div>
          <Rows
            rows={[
              ['30° 넘는 시간', t.usableStart ? `${t.usableStart}–${t.usableEnd}` : '오늘은 낮게 떠요'],
              ['가장 높을 때', `${t.highestAt} · ${t.highestAltitude}°`],
              ['달과 거리', `${t.moonSeparation}°`],
            ]}
          />
        </div>
        <FovPreview plan={plan} />
      </div>
    )
  } else if (field === 'framing' && plan.framing.value) {
    const f = plan.framing.value
    body = (
      <div className={styles.cellBody}>
        <div>
          <div className={styles.big}>{f.placement}</div>
          <Rows
            rows={[
              ['카메라 방향', f.rotation === null ? '지금 방향 그대로' : `${f.rotation}°`],
              ['대상 크기', f.fillPercent !== null ? `화면 가로의 약 ${f.fillPercent}%` : '-'],
            ]}
          />
        </div>
        <FovPreview plan={plan} />
      </div>
    )
  } else if (field === 'settings' && plan.settings.value) {
    const s = plan.settings.value
    body = (
      <Rows
        rows={[
          [
            '노출',
            <>
              {s.exposure}초{s.exposureRecommended && <span className={styles.rec}>추천</span>}
            </>,
          ],
          [
            '감도',
            <>
              ISO {s.iso}
              {s.isoRecommended && <span className={styles.rec}>추천</span>}
            </>,
          ],
          ['필터', s.filter],
          ['끝나는 조건', s.endCondition],
          ['예상 장수', `약 ${s.frames}장 (${s.start}–${s.end})`],
        ]}
      />
    )
  } else if (field === 'after' && plan.after.value) {
    const a = plan.after.value
    body = (
      <Rows
        rows={[
          ['적도의', a.mount],
          ['다크·플랫', a.calibration],
          ['장비', a.equipment],
        ]}
      />
    )
  }

  const editable = cell.state === 'set' && !disabled
  return (
    <section
      className={styles.cell}
      data-state={cell.state}
      aria-label={`${CELL_NAMES[field]} ${stateText}`}
      onClick={editable ? onEdit : undefined}
      onKeyDown={editable ? (e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), onEdit()) : undefined}
      tabIndex={editable ? 0 : undefined}
      role={editable ? 'button' : undefined}
      title={editable ? `${CELL_NAMES[field]} 고치기` : undefined}
    >
      <h3>
        {CELL_NAMES[field]}
        <span className={styles.stateText}>{stateText}</span>
      </h3>
      {body}
    </section>
  )
}

function Rows({ rows }: { rows: [string, ReactNode][] }) {
  return (
    <dl className={styles.rows}>
      {rows.map(([k, v]) => (
        <div key={k} className={styles.row}>
          <dt>{k}</dt>
          <dd>{v}</dd>
        </div>
      ))}
    </dl>
  )
}

/** 화각 안에 대상이 들어가는 모습: 점선 사각형 = 화면, 타원 = 대상 크기 */
function FovPreview({ plan }: { plan: Plan }) {
  const W = 118
  const Hh = Math.round((W * plan.fov.height) / plan.fov.width) || 80
  const size = plan.target.value?.sizeArcmin
  const r = size ? Math.max(2, ((size / 60 / plan.fov.width) * W) / 2) : 6
  const off =
    plan.framing.value && /비켜|왼|오른/.test(plan.framing.value.placement)
      ? (plan.framing.value.placement.includes('오른') ? 1 : -1) * W * 0.18
      : 0
  return (
    <svg className={styles.fov} viewBox={`0 0 ${W} ${Hh}`} aria-hidden="true">
      <rect x={1} y={1} width={W - 2} height={Hh - 2} rx={4} className={styles.fovFrame} />
      <ellipse cx={W / 2 + off} cy={Hh / 2} rx={r} ry={r * 0.72} className={styles.fovTarget} />
    </svg>
  )
}

function summary(plan: Plan) {
  const t = plan.target.value
  const s = plan.settings.value
  if (!t || !s) return ' '
  return `${t.name} ${t.koreanName ?? ''} · ${s.exposure}초 × 약 ${s.frames}장 · ISO ${s.iso} · ${s.filter}`
}

function summarySub(plan: Plan) {
  const s = plan.settings.value
  const a = plan.after.value
  if (!s || !a) return ' '
  return `${s.start} 시작 가능 · ${s.end} 종료 후 ${a.mount}`
}
