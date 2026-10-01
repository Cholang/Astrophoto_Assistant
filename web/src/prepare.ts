// 촬영 준비 상태 (서버 Prepare/PrepareRunner.cs와 같은 모양). 서버가 상태를 갖고 화면은 받아서 그리기만 한다.

export type PrepStatus = 'Pending' | 'Running' | 'Waiting' | 'Pass' | 'Fail' | 'Skipped'

export interface PrepAction {
  id: string
  label: string
  primary: boolean
}

export interface PrepStep {
  id: string
  title: string
  term: string | null
  status: PrepStatus
  hint: string
  message: string
  actions: PrepAction[]
  // 칸마다 다른 추가 정보 (가이딩 수치, 시험 사진 등)
  detail: Record<string, unknown> | null
}

export interface PrepView {
  started: boolean
  current: string | null
  steps: PrepStep[]
  ready: boolean
  version: number
}

/** 준비를 시작하고(같은 계획이면 이어서) 상태가 바뀔 때마다 onState. 끝내려면 돌려준 함수를 부른다 */
export function watchPrepare(onState: (v: PrepView) => void, onError: (message: string) => void): () => void {
  let source: EventSource | null = null
  let closed = false
  fetch('/api/prepare/start', { method: 'POST' })
    .then(async (r) => {
      if (closed) return
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        onError(body?.error ?? '촬영 준비를 시작하지 못했습니다.')
        return
      }
      onState((await r.json()) as PrepView)
      source = new EventSource('/api/prepare/watch')
      source.addEventListener('state', (e) => onState(JSON.parse((e as MessageEvent).data) as PrepView))
    })
    .catch(() => onError('내부 서버에 연결하지 못했습니다.'))
  return () => {
    closed = true
    source?.close()
  }
}

/** 칸의 버튼. 받을 수 없으면 이유를 돌려준다 */
export async function prepareAct(step: string, action: string): Promise<string | null> {
  try {
    const r = await fetch('/api/prepare/act', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ step, action }),
    })
    if (r.ok) return null
    const body = (await r.json().catch(() => null)) as { error?: string } | null
    return body?.error ?? '버튼을 처리하지 못했습니다.'
  } catch {
    return '내부 서버에 연결하지 못했습니다.'
  }
}
