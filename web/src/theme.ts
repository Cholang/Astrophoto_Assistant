import { useCallback, useEffect, useState } from 'react'
import { tellHost } from './host'

export type Theme = 'dark' | 'light' | 'night'

export const THEMES: { value: Theme; label: string }[] = [
  { value: 'dark', label: '어둡게' },
  { value: 'light', label: '밝게' },
  { value: 'night', label: '촬영' },
]

const KEY = 'app.theme'

function load(): Theme {
  try {
    const v = localStorage.getItem(KEY)
    if (v === 'dark' || v === 'light' || v === 'night') return v
  } catch {
    /* 저장소를 못 쓰면 기본값 */
  }
  return 'dark'
}

/** 데스크톱 창(WPF)이 로딩 순간의 배경색을 테마에 맞출 수 있게 알려 준다. */
function tellHostTheme(theme: Theme) {
  tellHost({ type: 'theme', theme })
}

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(load)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    try {
      localStorage.setItem(KEY, theme)
    } catch {
      /* 무시 */
    }
    tellHostTheme(theme)
  }, [theme])

  const cycle = useCallback(
    () => setTheme((t) => THEMES[(THEMES.findIndex((x) => x.value === t) + 1) % THEMES.length].value),
    [],
  )

  // 단축키 Ctrl+Alt+N: 테마 순환
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.altKey && e.key.toLowerCase() === 'n') {
        e.preventDefault()
        cycle()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [cycle])

  return { theme, setTheme }
}
