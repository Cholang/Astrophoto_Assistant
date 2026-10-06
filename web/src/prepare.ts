// 준비 묶음 상태 (서버 Prepare/Flow/PrepView.cs와 같은 모양). 서버가 상태를 갖고 화면은 받아서 그리기만 한다.
// 묶음 두 개 (DESIGN.md "단계 재구성"): rig = 장비 준비(극축 정렬 · 캘리브레이션 · 초점), target = 대상(이동 · 센터링 · 초점 확인 · 가이딩 · 시험 사진)
// 화면 영역 이름(DESIGN.md "촬영 준비"): 안내(guide) · 중앙 정보(center: 측정값 → 상태 줄 → 버튼) · 하늘 화면(live) · 진행 표시(tasks)

export type PrepGroup = 'rig' | 'target'

export type PrepTaskStatus = 'Pending' | 'Running' | 'Waiting' | 'Done' | 'Failed' | 'Skipped' | 'NeedsRecheck'
export type Tone = 'None' | 'Busy' | 'Ok' | 'Warn' | 'Fail'
export type Freshness = 'Fresh' | 'Stale' | 'Unverified'

export interface PrepAction {
  id: string
  label: string
  primary: boolean
}

export interface ReadoutView {
  kind: string
  big: string
  caption: string
  tone: Tone
  values: Record<string, number>
  observedAt: string
  freshness: Freshness
}

export interface CurrentView {
  taskId: string
  runId: number
  subSteps: { id: string; label: string; status: PrepTaskStatus }[]
  guide: { title: string; text: string }
  center: { readout: ReadoutView | null; status: { text: string; tone: Tone } | null; actions: PrepAction[] }
  live: { kind: string; url: string | null; data: unknown; observedAt: string } | null
}

export interface PrepView {
  started: boolean
  tasks: { id: string; title: string; status: PrepTaskStatus; result: string | null }[]
  current: CurrentView | null
  ready: boolean
  version: number
  /** 모의 장비면 하늘 화면을 그림으로 흉내, 아니면 live.url의 실제 이미지 */
  simulated: boolean
  group: PrepGroup
  /** 다른 묶음이 장비를 쓰는 동안 멈춰 둠: 화면이 그 묶음으로 간다 (예: 가이딩 → 캘리브레이션 다시 → rig) */
  handoff: PrepGroup | null
}

/** 묶음을 시작하고(같은 밤·같은 계획이면 이어서) 상태가 바뀔 때마다 onState. 끝내려면 돌려준 함수를 부른다 */
export function watchPrepare(group: PrepGroup, onState: (v: PrepView) => void, onError: (message: string) => void): () => void {
  let source: EventSource | null = null
  let closed = false
  fetch(`/api/prepare/${group}/start`, { method: 'POST' })
    .then(async (r) => {
      if (closed) return
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        onError(body?.error ?? (group === 'rig' ? '장비 준비를 시작하지 못했습니다.' : '대상 단계를 시작하지 못했습니다.'))
        return
      }
      onState((await r.json()) as PrepView)
      source = new EventSource(`/api/prepare/${group}/watch`)
      source.addEventListener('state', (e) => onState(JSON.parse((e as MessageEvent).data) as PrepView))
    })
    .catch(() => onError('내부 서버에 연결하지 못했습니다.'))
  return () => {
    closed = true
    source?.close()
  }
}

/** 버튼. 받을 수 없으면 이유를 돌려준다 */
export async function prepareAct(group: PrepGroup, action: string): Promise<string | null> {
  try {
    const r = await fetch(`/api/prepare/${group}/act`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ action }),
    })
    if (r.ok) return null
    const body = (await r.json().catch(() => null)) as { error?: string } | null
    return body?.error ?? '버튼을 처리하지 못했습니다.'
  } catch {
    return '내부 서버에 연결하지 못했습니다.'
  }
}

/** 묶음의 지금 상태 (시작하지 않음). 못 읽으면 null */
export async function prepareState(group: PrepGroup): Promise<PrepView | null> {
  try {
    const r = await fetch(`/api/prepare/${group}/state`)
    return r.ok ? ((await r.json()) as PrepView) : null
  } catch {
    return null
  }
}

/** 중단 (진행 표시 아래 버튼) */
export function prepareAbort(group: PrepGroup) {
  void fetch(`/api/prepare/${group}/abort`, { method: 'POST' })
}
