// 그날 밤 진행 기록 (서버 Session/NightSession.cs). AA가 중간에 꺼져도 다시 켜면 이어서 할지 묻는다 (2026-10-08)

/** resume = 이어서 할지 묻기, notice = 중단된 촬영이 있었다고 알리기만 */
export interface PendingSession {
  kind: 'resume' | 'notice'
  summary: string
  phase: string | null
}

/** 화면이 지금 단계를 서버에 알린다 (장비 준비 이후 단계만 기록됨) */
export function reportPhase(phase: string, profileId: string | null) {
  void fetch('/api/session/phase', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ phase, profileId }),
  }).catch(() => null)
}

/** 다시 켰을 때 물을 것 (없으면 null) */
export async function pendingSession(profileId: string | null): Promise<PendingSession | null> {
  try {
    const r = await fetch(`/api/session/pending${profileId ? `?profileId=${encodeURIComponent(profileId)}` : ''}`)
    return r.status === 200 ? ((await r.json()) as PendingSession) : null
  } catch {
    return null
  }
}

/**
 * 이어서: 서버가 장비를 멈추고(노출·자동초점·적도의 이동) 계획·기록을 되살린 뒤, 끝낸 작업을 다시 확인해 갈 화면을 돌려준다.
 * 장비를 멈추지 못했으면 error (다시 시도하게)
 */
export async function resumeSession(): Promise<{ phase: string } | { error: string }> {
  try {
    const r = await fetch('/api/session/resume', { method: 'POST' })
    const body = (await r.json().catch(() => null)) as { phase?: string; error?: string } | null
    if (r.ok && body?.phase) return { phase: body.phase }
    return { error: body?.error ?? '이어서 하지 못했습니다.' }
  } catch {
    return { error: '내부 서버에 연결하지 못했습니다.' }
  }
}

/** 새로 시작 / 알림 확인: 다시 묻지 않는다 */
export function dismissSession() {
  void fetch('/api/session/dismiss', { method: 'POST' }).catch(() => null)
}
