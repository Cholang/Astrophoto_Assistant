import { useRef, type KeyboardEvent } from 'react'
import { THEMES, type Theme } from '../theme'
import styles from './ThemeSwitch.module.css'

/**
 * 화면 모드 라디오 그룹: 같은 너비 버튼 세 개.
 * 라디오 규칙대로 선택된 버튼만 Tab으로 들어오고, ←/→로 옮기면 바로 바뀐다.
 */
export default function ThemeSwitch({ value, onChange }: { value: Theme; onChange: (t: Theme) => void }) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])

  const move = (e: KeyboardEvent, index: number) => {
    const step = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0
    if (!step) return
    e.preventDefault()
    const next = (index + step + THEMES.length) % THEMES.length
    onChange(THEMES[next].value)
    refs.current[next]?.focus()
  }

  return (
    <div className={styles.group} role="radiogroup" aria-label="화면 모드 (Ctrl+Alt+N)">
      {THEMES.map((t, i) => {
        const checked = t.value === value
        return (
          <button
            key={t.value}
            ref={(el) => {
              refs.current[i] = el
            }}
            type="button"
            role="radio"
            aria-checked={checked}
            tabIndex={checked ? 0 : -1}
            className={styles.option}
            onClick={() => onChange(t.value)}
            onKeyDown={(e) => move(e, i)}
          >
            {t.label}
          </button>
        )
      })}
    </div>
  )
}
