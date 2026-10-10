/**
 * 제품 이름. 값은 저장소의 product.json에서 빌드할 때 들어온다 (vite.config.ts의 define).
 * 이름이 바뀌어도 코드는 고치지 않는다 — product.json만 고친다.
 * 문장에 넣을 때는 조사가 이름에 맞게 바뀌도록 ga·reul·neun·wa를 쓴다 (예: `${PRODUCT.ga} 켭니다`).
 */
declare const __PRODUCT__: { name: string; fullName: string; dataFolder: string; version: string }
declare const __VERSION__: string

/** 앱 버전 표기 (예: v0.0.27). product.json의 version + 빌드 번호(커밋 수), vite.config.ts에서 만든다 */
export const VERSION = __VERSION__

/** 앞말의 받침 유무. 영문·숫자는 읽는 소리로 판단한다 (서버 Astro.Core.Josa와 같은 규칙). */
export function hasFinalConsonant(word: string) {
  const s = word.trimEnd()
  if (!s) return false
  const c = s[s.length - 1]
  const code = c.charCodeAt(0)
  if (code >= 0xac00 && code <= 0xd7a3) return (code - 0xac00) % 28 !== 0
  if (/[a-z]/i.test(c)) return 'LMNRlmnr'.includes(c) // 엘·엠·엔·알
  if (/[0-9]/.test(c)) return '013678'.includes(c) // 영·일·삼·육·칠·팔
  return false
}

const josa = (word: string, withFinal: string, withoutFinal: string) =>
  word + (hasFinalConsonant(word) ? withFinal : withoutFinal)

const { name, fullName } = __PRODUCT__

export const PRODUCT = {
  name,
  fullName,
  /** "AA가" / "별빛이" */
  ga: josa(name, '이', '가'),
  /** "AA를" / "별빛을" */
  reul: josa(name, '을', '를'),
  /** "AA는" / "별빛은" */
  neun: josa(name, '은', '는'),
  /** "AA와" / "별빛과" */
  wa: josa(name, '과', '와'),
}
