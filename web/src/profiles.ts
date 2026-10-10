/** 서버 Astro.Server.Profiles.Profile과 같은 모양 */
export interface Profile {
  id: string
  nickname: string
  memo: string | null
  hasImage: boolean
  createdAt: string
  lastUsedAt: string | null
  /** 이 사람의 관측지 목록 (최대 SITES_MAX). 예전 파일은 null */
  sites: ObservingSite[] | null
}

/** 서버 ObservingSite와 같은 모양. 위도·경도는 도, 고도는 m */
export interface ObservingSite {
  id: string
  name: string
  latitude: number
  longitude: number
  elevation: number
}

export const SITES_MAX = 10
export const SITE_NAME_MAX_BYTES = 30 // 한글 15자

async function profileResult(res: Response, fallback: string): Promise<Profile> {
  const body = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(body.error ?? fallback)
  return body
}

export async function editProfile(id: string, nickname: string, memo: string): Promise<Profile> {
  const res = await fetch(`/api/profiles/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nickname, memo }),
  })
  return profileResult(res, '프로필을 고치지 못했습니다.')
}

/** 프로필 삭제 (사진도 함께) */
export async function deleteProfile(id: string): Promise<void> {
  const res = await fetch(`/api/profiles/${id}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 404) throw new Error('프로필을 삭제하지 못했습니다.')
}

/** 관측지 추가. 고도를 모르면 비워 두면 서버가 좌표로 조회한다 */
export async function addSite(id: string, site: { name: string; latitude: number; longitude: number; elevation?: number }): Promise<Profile> {
  const res = await fetch(`/api/profiles/${id}/sites`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(site),
  })
  return profileResult(res, '관측지를 저장하지 못했습니다.')
}

/** 관측지 이름 고치기 (좌표는 고치지 않는다) */
export async function renameSite(id: string, siteId: string, name: string): Promise<Profile> {
  const res = await fetch(`/api/profiles/${id}/sites/${siteId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, latitude: 0, longitude: 0 }),
  })
  return profileResult(res, '관측지 이름을 고치지 못했습니다.')
}

export async function removeSite(id: string, siteId: string): Promise<Profile> {
  return profileResult(await fetch(`/api/profiles/${id}/sites/${siteId}`, { method: 'DELETE' }), '관측지를 지우지 못했습니다.')
}

// 글자 수는 byte로 센다: 한글 등은 2, 영문·숫자·기호는 1 (서버 ProfileStore.TextBytes와 같은 규칙).
export const NICKNAME_MAX_BYTES = 20 // 한글 10자
export const MEMO_MAX_BYTES = 120 // 한글 60자
const IMAGE_SIZE = 256

export function textBytes(text: string) {
  let n = 0
  for (let i = 0; i < text.length; i++) n += text.charCodeAt(i) < 0x80 ? 1 : 2
  return n
}

/** 최대 byte를 넘는 뒷부분을 잘라 낸다. */
export function clampBytes(text: string, max: number) {
  let n = 0
  for (let i = 0; i < text.length; i++) {
    n += text.charCodeAt(i) < 0x80 ? 1 : 2
    if (n > max) return text.slice(0, i)
  }
  return text
}

export async function listProfiles(): Promise<Profile[]> {
  const res = await fetch('/api/profiles')
  if (!res.ok) throw new Error('프로필 목록을 불러오지 못했습니다.')
  return res.json()
}

export async function createProfile(nickname: string, memo: string): Promise<Profile> {
  const res = await fetch('/api/profiles', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nickname, memo }),
  })
  const body = await res.json()
  if (!res.ok) throw new Error(body.error ?? '프로필을 만들지 못했습니다.')
  return body
}

export async function selectProfile(id: string) {
  await fetch(`/api/profiles/${id}/select`, { method: 'POST' })
}

export async function uploadProfileImage(id: string, image: Blob) {
  const res = await fetch(`/api/profiles/${id}/image`, { method: 'PUT', headers: { 'Content-Type': 'image/webp' }, body: image })
  if (!res.ok) throw new Error('프로필 이미지를 저장하지 못했습니다.')
}

export const profileImageUrl = (p: Profile) => `/api/profiles/${p.id}/image?v=${encodeURIComponent(p.lastUsedAt ?? p.createdAt)}`

/**
 * 고른 사진을 왜곡 없는 정사각형 프로필 이미지로 만든다.
 * - 사진의 회전 정보(EXIF)를 반영해 디코딩
 * - 가운데를 정사각형으로 잘라 256×256으로 축소 (늘이지 않음)
 * - WebP로 다시 저장해 원본 크기·형식과 관계없이 작게 통일
 */
export async function toSquareProfileImage(file: File): Promise<Blob> {
  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error('이 형식은 열 수 없습니다. JPG나 PNG 이미지로 골라 주세요.')
  }
  const side = Math.min(bitmap.width, bitmap.height)
  const sx = (bitmap.width - side) / 2
  const sy = (bitmap.height - side) / 2

  const canvas = document.createElement('canvas')
  canvas.width = IMAGE_SIZE
  canvas.height = IMAGE_SIZE
  const ctx = canvas.getContext('2d')!
  ctx.imageSmoothingQuality = 'high'
  ctx.drawImage(bitmap, sx, sy, side, side, 0, 0, IMAGE_SIZE, IMAGE_SIZE)
  bitmap.close()

  return new Promise((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('이미지를 만들지 못했습니다.'))), 'image/webp', 0.9),
  )
}

/**
 * 전원 배선 (docs/POWER_LAYOUT_PLAN.md): 장비 종류 → 전원을 받는 곳. 'dc' DC 전원 · 'mount' 적도의(새들 포트) · 'switch' 파워박스 · null 자체 전원(배터리 등).
 * 아이라가 믿고 쓰는 값이고, 실제와 다르면 아이라가 조용히 고친다
 */
export type PowerSource = 'dc' | 'mount' | 'switch' | null
export type PowerWiring = Record<string, PowerSource>

/** 설정하지 않았을 때 = 서버 기본값과 같음 (적도의 먼저, 카메라는 자체 전원) */
export const DEFAULT_WIRING: PowerWiring = {
  mount: 'dc', switch: 'mount', camera: null, focuser: 'switch', filterwheel: 'switch',
  rotator: 'switch', guider: 'switch', heater: 'switch', flatdevice: 'switch',
}

export async function getPower(id: string): Promise<{ sources: PowerWiring; custom: boolean }> {
  const res = await fetch(`/api/profiles/${id}/power`)
  if (!res.ok) throw new Error('전원 배선을 읽지 못했습니다.')
  return res.json()
}

export async function savePower(id: string, sources: PowerWiring): Promise<void> {
  const res = await fetch(`/api/profiles/${id}/power`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sources }),
  })
  if (!res.ok) throw new Error(((await res.json().catch(() => null)) as { error?: string } | null)?.error ?? '전원 배선을 저장하지 못했습니다.')
}
