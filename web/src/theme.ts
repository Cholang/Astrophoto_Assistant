import { useCallback, useEffect, useLayoutEffect, useState } from 'react'
import { tellHost } from './host'

// 테마는 두 가지 (2026-10-10 사용자 결정 — 밝게 테마 없앰): 기본(값 dark — 짙은 회색) · 다크(값 night — 촬영용 적색). 값 이름은 저장된 설정과 호환을 위해 그대로
export type Theme = 'dark' | 'night'

export const THEMES: { value: Theme; label: string }[] = [
  { value: 'dark', label: '기본' },
  { value: 'night', label: '다크' },
]

const KEY = 'app.theme'

function load(): Theme {
  try {
    const v = localStorage.getItem(KEY)
    if (v === 'dark' || v === 'night') return v // 예전에 고른 'light'는 기본으로
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

  // 그리기 전에 바꾼다: 테마 버튼과 바탕·글자 색이 같은 프레임에 바뀌게 (2026-10-10 — 한 박자 늦게 바뀌었음)
  useLayoutEffect(() => {
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
