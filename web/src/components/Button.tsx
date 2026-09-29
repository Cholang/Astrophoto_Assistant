import type { ButtonHTMLAttributes } from 'react'
import styles from './Button.module.css'

/**
 * primary: 한 화면에 하나뿐인 주 행동 (DESIGN.md 1장)
 * quiet: 보조 행동. 테두리만 있는 가벼운 버튼
 */
type Props = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'quiet' }

export default function Button({ variant = 'quiet', className, type = 'button', ...rest }: Props) {
  return <button type={type} className={[styles.button, styles[variant], className].filter(Boolean).join(' ')} {...rest} />
}
