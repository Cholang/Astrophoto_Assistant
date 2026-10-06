// 촬영 상태 (서버 Shoot/ShootSession.cs의 ShootView와 같은 모양). 서버가 찍고, 화면은 받아서 그리기만 한다.

import type { Tone } from './prepare'

export type ShootMode = 'Idle' | 'Shoot' | 'Dither' | 'Flip' | 'Focus' | 'Paused' | 'Finishing' | 'Ended'

export interface ShootView {
  started: boolean
  target: string | null
  planned: number
  good: number
  excluded: number
  exposureSeconds: number
  iso: number
  elapsed: number
  mode: ShootMode
  /** 자오선 반전 단계: 0 반전 · 1 다시 센터링 · 2 가이딩 재시작 */
  flipStep: number
  flipAt: string | null
  /** 자오선 반전까지 남은 분 (반전을 마쳤거나 모르면 null) */
  flipInMinutes: number | null
  guide: number[]
  hfr: number[]
  focusHfr: number
  pixelScale: number
  lastGrade: string | null
  tally: Record<string, number>
  /** 등급별 분류 기준 (툴팁) */
  criteria: Record<string, string>
  note: { text: string; tone: Tone } | null
  focusPoints: { pos: number; hfr: number }[] | null
  /** 끝 이유: done(계획 장수) · low(대상이 낮아짐) · dawn(새벽) · user(촬영 중단) · error */
  ended: string | null
  endAt: string | null
  photoUrl: string | null
  photoAt: string | null
  version: number
  simulated: boolean
  /** 가이드 별을 잃어 멈춘 원인: light · cloud · jump · guider (멈추지 않았으면 null) */
  pause: string | null
  pausedSeconds: number
  guideExposureMs: number
}

/** 촬영을 시작하고(찍는 중이면 이어서) 상태가 바뀔 때마다 onState. 끝내려면 돌려준 함수를 부른다 */
export function watchShoot(onState: (v: ShootView) => void, onError: (message: string) => void): () => void {
  let source: EventSource | null = null
  let closed = false
  fetch('/api/shoot/start', { method: 'POST' })
    .then(async (r) => {
      if (closed) return
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        onError(body?.error ?? '촬영을 시작하지 못했습니다.')
        return
      }
      onState((await r.json()) as ShootView)
      source = new EventSource('/api/shoot/watch')
      source.addEventListener('state', (e) => onState(JSON.parse((e as MessageEvent).data) as ShootView))
    })
    .catch(() => onError('내부 서버에 연결하지 못했습니다.'))
  return () => {
    closed = true
    source?.close()
  }
}

/** "촬영 중단": 지금 사진까지 찍고 멈춘다. 받을 수 없으면 이유 */
export async function stopShoot(): Promise<string | null> {
  try {
    const r = await fetch('/api/shoot/stop', { method: 'POST' })
    if (r.ok) return null
    const body = (await r.json().catch(() => null)) as { error?: string } | null
    return body?.error ?? '촬영을 멈추지 못했습니다.'
  } catch {
    return '내부 서버에 연결하지 못했습니다.'
  }
}

/** 오늘 밤 요약 (마무리 끝) */
export interface NightSummary {
  targets: { targetId: string; name: string; good: number; excluded: number; exposureSeconds: number; iso: number; folder: string | null; pausedMinutes: Record<string, number> | null }[]
  tally: Record<string, number>
  folder: string | null
  flat: { skipped: boolean; count: number; exposureSeconds: number } | null
  dark: { skipped: boolean; flatDarks: number; sets: { exposureSeconds: number; iso: number; count: number }[]; homed: boolean } | null
  pack: { homed: boolean; disconnected: boolean; closed: boolean } | null
}

export async function nightSummary(): Promise<NightSummary | null> {
  try {
    const r = await fetch('/api/night/summary')
    return r.ok ? ((await r.json()) as NightSummary) : null
  } catch {
    return null
  }
}

/** 시간 (예: 2시간 24분 · 1시간 · 45분) */
export function hm(seconds: number) {
  const m = Math.round(seconds / 60)
  const h = Math.floor(m / 60)
  if (h && !(m % 60)) return `${h}시간`
  return (h ? `${h}시간 ` : '') + `${m % 60}분`
}

export const clock = (iso: string | null) =>
  iso ? new Date(iso).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false }) : ''
