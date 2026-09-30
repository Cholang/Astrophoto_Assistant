// 촬영 계획 화면과 서버(/api/plan) 사이의 모양. 서버 PlanView·NightChart와 같게 유지한다.

export type CellState = 'empty' | 'asking' | 'set'

export interface PlanTarget {
  name: string
  commonName: string | null
  koreanName: string | null
  type: string
  constellation: string
  sizeArcmin: number | null
  usableStart: string | null
  usableEnd: string | null
  highestAt: string
  highestAltitude: number
  transit: string | null
  moonSeparation: number
}

export interface PlanFraming {
  placement: string
  rotation: number
  fillPercent: number | null
}

export interface PlanSettings {
  exposure: number
  iso: number
  filter: string
  endCondition: string
  start: string
  end: string
  frames: number
  exposureRecommended: boolean
  isoRecommended: boolean
}

export interface PlanAfter {
  mount: string
  calibration: string
  equipment: string
}

interface Cell<T> {
  state: CellState
  value: T | null
}

export interface Plan {
  asking: string | null
  complete: boolean
  fov: { width: number; height: number }
  target: Cell<PlanTarget>
  framing: Cell<PlanFraming>
  settings: Cell<PlanSettings>
  after: Cell<PlanAfter>
}

/** 그래프 자료. 시각은 모두 시간축 시작부터의 분 */
export interface Chart {
  start: string
  minutes: number
  hours: { at: number; label: string }[]
  dark: { start: number; end: number; label: string }
  sun: { set: number; rise: number }
  moon: { illumination: number; up: { start: number; end: number; setLabel: string; riseLabel: string }[] }
  clouds: { at: number; cover: number }[]
  target: { name: string; track: [number, number][]; transit: number | null; transitLabel: string | null } | null
  shooting: { start: number; end: number } | null
}

export interface Message {
  /** notice: 연습 대화로 넘어감 등 안내 한 줄 */
  role: 'assistant' | 'user' | 'notice'
  text: string
  choices?: string[] | null
  /** 이 메시지의 선택지 중 사용자가 고른 것 */
  picked?: string
}

export interface PlanState {
  error?: string
  messages: Message[]
  plan: Plan
  chart: Chart
  catalogAvailable: boolean
  cloudsAvailable: boolean
}

export type ChatEvent =
  | { type: 'text'; text: string }
  | { type: 'plan'; plan: Plan }
  | { type: 'choices'; choices: string[] }
  | { type: 'notice'; message: string }
  | { type: 'error'; message: string }
  | { type: 'done' }

/** 사용자 말 하나를 보내고, 서버가 보내는 이벤트(SSE)를 하나씩 넘겨준다 */
export async function sendChat(text: string, onEvent: (e: ChatEvent) => void): Promise<void> {
  const res = await fetch('/api/plan/chat', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ text }),
  })
  if (!res.ok || !res.body) throw new Error(`서버 응답 ${res.status}`)
  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value
    let cut: number
    while ((cut = buffer.indexOf('\n\n')) >= 0) {
      const block = buffer.slice(0, cut)
      buffer = buffer.slice(cut + 2)
      let type = 'message'
      let data = ''
      for (const line of block.split('\n')) {
        if (line.startsWith('event:')) type = line.slice(6).trim()
        else if (line.startsWith('data:')) data += line.slice(5).trim()
      }
      if (!data) continue
      const payload = JSON.parse(data)
      if (type === 'plan') onEvent({ type, plan: payload })
      else onEvent({ type, ...payload } as ChatEvent)
    }
  }
}
