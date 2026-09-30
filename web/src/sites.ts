import type { ObservingSite, Profile } from './profiles'

/**
 * 관측지 (DESIGN.md 3장 "관측지"). 기준은 AA(프로필의 관측지 목록)이고 N.I.N.A.는 AA 값을 따라간다.
 * 지금 관측지 = N.I.N.A.에 설정된 좌표. 서버 SiteService와 같이 쓴다.
 */
export interface CurrentSite {
  latitude: number
  longitude: number
  elevation: number
}

/** 지금 N.I.N.A.에 설정된 관측지. 없으면(위도·경도 0) null */
export async function fetchCurrentSite(): Promise<CurrentSite | null> {
  const res = await fetch('/api/site/current')
  if (res.status === 204 || !res.ok) return null
  return res.json()
}

/** 같은 장소로 볼 거리: 약 50m (위도·경도 0.0005°) */
const SAME = 0.0005
export const sameSpot = (a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }) =>
  Math.abs(a.latitude - b.latitude) < SAME && Math.abs(a.longitude - b.longitude) < SAME

/**
 * 지금 관측지를 프로필 목록과 맞춰 본다.
 * - saved: 이 프로필 목록에 있는 관측지 (없으면 null)
 * - otherName: 이 프로필에는 없지만 다른 프로필에 저장된 이름 (프로필을 바꾼 경우. 저장 안 됨으로 표시)
 */
export function describeSite(current: CurrentSite | null, profile: Profile | null, all: Profile[]) {
  if (!current) return { saved: null, otherName: null }
  const saved = profile?.sites?.find((s) => sameSpot(s, current)) ?? null
  const otherName = saved
    ? null
    : all.flatMap((p) => p.sites ?? []).find((s) => sameSpot(s, current))?.name ?? null
  return { saved, otherName }
}

/** 화면 표시: "위도 37.52° · 경도 127.05°" (소수 둘째 자리 ≈ 1km) */
export const coordText = (s: { latitude: number; longitude: number }, digits = 2) =>
  `위도 ${s.latitude.toFixed(digits)}° · 경도 ${s.longitude.toFixed(digits)}°`

/** 입력칸·붙여넣기용: "37.62310, 128.74320" */
export const coordValue = (s: { latitude: number; longitude: number }) => `${s.latitude.toFixed(5)}, ${s.longitude.toFixed(5)}`

/**
 * 붙여 넣은 좌표를 읽는다. 카카오맵·네이버지도·구글지도에서 복사한 모양을 받는다:
 * "37.6231, 128.7432" · "37.6231 128.7432" · "N 37° 37' 23.2\", E 128° 44' 35.5\"" · "37°37'23.2\"N 128°44'35.5\"E"
 */
export function parseCoords(text: string): { latitude: number; longitude: number } | null {
  const t = text.trim()
  if (!t) return null
  // 도·분·초
  const dms = [...t.matchAll(/([NSEW북남동서])?\s*(-?\d+(?:\.\d+)?)\s*[°º˚]\s*(?:(\d+(?:\.\d+)?)\s*['′’]\s*)?(?:(\d+(?:\.\d+)?)\s*["″”]\s*)?([NSEW북남동서])?/gi)]
  if (dms.length >= 2) {
    const toDeg = (m: RegExpMatchArray) => {
      const v = Math.abs(+m[2]) + (+(m[3] ?? 0)) / 60 + (+(m[4] ?? 0)) / 3600
      const hemi = (m[1] ?? m[5] ?? '').toUpperCase()
      const neg = +m[2] < 0 || hemi === 'S' || hemi === 'W' || hemi === '남' || hemi === '서'
      return { v: neg ? -v : v, hemi }
    }
    const a = toDeg(dms[0])
    const b = toDeg(dms[1])
    const aIsLon = /[EW동서]/i.test(a.hemi)
    const r = aIsLon ? { latitude: b.v, longitude: a.v } : { latitude: a.v, longitude: b.v }
    return valid(r) ? r : null
  }
  const nums = t.match(/-?\d+(?:\.\d+)?/g)
  if (!nums || nums.length < 2) return null
  const r = { latitude: +nums[0], longitude: +nums[1] }
  return valid(r) ? r : null
}

const valid = (r: { latitude: number; longitude: number }) =>
  Number.isFinite(r.latitude) && Number.isFinite(r.longitude) && Math.abs(r.latitude) <= 90 && Math.abs(r.longitude) <= 180 && !(r.latitude === 0 && r.longitude === 0)

export async function elevationOf(latitude: number, longitude: number): Promise<number | null> {
  try {
    const res = await fetch(`/api/site/elevation?lat=${latitude}&lon=${longitude}`)
    if (!res.ok) return null
    return (await res.json()).elevation as number
  } catch {
    return null
  }
}

/**
 * 관측지 적용: N.I.N.A.에 저장 → 적도의에 위치 보내기. 단계 문장을 onStep으로 알린다.
 * 모두 되면 true, 실패하면 실패 문장과 함께 false
 */
export function applySite(
  site: { latitude: number; longitude: number; elevation?: number },
  onStep: (message: string) => void,
): Promise<{ ok: boolean; message: string }> {
  return new Promise((resolve) => {
    const q = `lat=${site.latitude}&lon=${site.longitude}${site.elevation != null ? `&elev=${site.elevation}` : ''}`
    const source = new EventSource(`/api/site/apply?${q}`)
    let last = ''
    let failed: string | null = null
    source.addEventListener('check', (e) => {
      const r = JSON.parse((e as MessageEvent).data) as { status: string; message: string; diagnosis?: { fix?: string } | null }
      last = r.message
      if (r.status === 'Fail') failed = r.diagnosis?.fix ? `${r.message}. ${r.diagnosis.fix}` : r.message
      onStep(r.message)
    })
    // 서버가 다 보내고 닫으면 끝
    source.onerror = () => {
      source.close()
      resolve(failed ? { ok: false, message: failed } : last ? { ok: true, message: last } : { ok: false, message: '관측지를 저장하지 못했습니다' })
    }
  })
}

/** 서버 설정 (카카오맵 키 등) */
let config: Promise<{ kakaoKey: string | null }> | null = null
export function appConfig() {
  config ??= fetch('/api/config').then((r) => (r.ok ? r.json() : { kakaoKey: null }), () => ({ kakaoKey: null }))
  return config
}

export type { ObservingSite }
