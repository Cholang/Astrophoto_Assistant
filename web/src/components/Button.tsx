import type { ComponentProps } from 'react'
import styles from './Button.module.css'

/**
 * 앱의 모든 글자 버튼 (DESIGN.md 3장 "버튼"). 모양은 이 파일과 Button.module.css 한 곳에서만 정한다 —
 * 화면은 className으로 자리·너비만 준다(색·테두리·높이·호버는 넘기지 않는다).
 * variant  primary: 한 화면에 하나뿐인 주 행동 (DESIGN.md 1장) / quiet: 보조 행동, 테두리만 / ghost: 테두리도 없는 흐린 글자(레일 아래 홈·종료처럼 늘 있는 도구)
 * size     sm 34px(목록 안) / md 42px(기본) / lg 56px(하늘 화면 중앙 정보·덱 아래처럼 큰 자리)
 * surface  app: 앱 바탕 위 / sky: 하늘 화면(사진) 위 — 어두운 반투명 바탕과 별빛 글자
 * 아이콘은 children에 함께 넣으면 글자 왼쪽에 18px로 놓인다.
 * 지금은 못 쓰지만 이유를 보여 줘야 하면(가리키면 title) disabled 대신 aria-disabled — 흐리게 + 금지 커서, 누르는 쪽에서 무시한다.
 */
type Props = ComponentProps<'button'> & {
  variant?: 'primary' | 'quiet' | 'ghost'
  size?: 'sm' | 'md' | 'lg'
  surface?: 'app' | 'sky'
}

export default function Button({ variant = 'quiet', size = 'md', surface = 'app', className, type = 'button', ...rest }: Props) {
  return (
    <button
      type={type}
      className={[styles.button, styles[variant], className].filter(Boolean).join(' ')}
      data-size={size}
      data-surface={surface}
      {...rest}
    />
  )
}
