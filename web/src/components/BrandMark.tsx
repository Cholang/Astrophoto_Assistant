import { useMemo } from 'react'
import mark from '../assets/aira-mark.svg?raw'
import { scopedSvg } from '../inlineSvg'
import { PRODUCT } from '../product'
import type { Theme } from '../theme'

/**
 * 상태 줄의 이름 자리: 로고(assets/aira-logo.svg)의 AIRA 별자리 글자만 떼어 낸 그림 (2026-10-10 사용자 요청).
 * 작은 크기라 선만 원본보다 굵게. 테마마다 색이 바뀌어 그 자리에 넣는다(인라인). 읽는 이름은 product.json 이름
 */
export default function BrandMark({ theme, className }: { theme: Theme; className?: string }) {
  // 그림 안의 스타일·id는 그림 안에 가둔다 (inlineSvg — 앱 아이콘 색이 바뀌던 버그)
  const html = useMemo(() => scopedSvg(mark, 'aira-mark', theme), [theme])
  return <span className={className} role="img" aria-label={PRODUCT.name} dangerouslySetInnerHTML={{ __html: html }} />
}
