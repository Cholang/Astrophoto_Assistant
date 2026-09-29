/** 서버 Astro.Server.Profiles.Profile과 같은 모양 */
export interface Profile {
  id: string
  nickname: string
  memo: string | null
  hasImage: boolean
  createdAt: string
  lastUsedAt: string | null
}

export const NICKNAME_MAX = 20
export const MEMO_MAX = 60
const IMAGE_SIZE = 256

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
