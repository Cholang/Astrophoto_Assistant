import { createContext, useContext, useEffect, type ReactNode } from 'react'

/**
 * 화면 바탕을 하늘(검은 사진 바탕)로 깔지 — 화면이 직접 App에 알린다 (2026-10-10 레일에서 분리.
 * 전에는 진행 표시(레일)에 넘기는 값에 섞여 있어, 레일을 숨기거나 레일 값을 안 보내는 화면에서 바탕이 바뀌었다).
 * 하늘 바탕이면 App이 화면 영역과 진행 표시 뒤를 함께 하늘색으로 칠한다
 */
const SkyContext = createContext<(on: boolean) => void>(() => {})

export function ScreenSkyProvider({ onChange, children }: { onChange: (on: boolean) => void; children: ReactNode }) {
  return <SkyContext.Provider value={onChange}>{children}</SkyContext.Provider>
}

/** 화면에서: 지금 하늘 바탕이 필요한가 (화면을 떠나면 끔) */
export function useScreenSky(on: boolean) {
  const set = useContext(SkyContext)
  useEffect(() => set(on), [on, set])
  useEffect(() => () => set(false), [set])
}
