import { createContext } from 'react'

/**
 * 화면 전환(페이드)이 끝났는지 (DESIGN.md 9장 "전환 중에는 누를 수 없다").
 * 전환 중에는 App이 화면 전체를 누를 수 없게 막고, 화면은 이 값이 true가 된 뒤에 들어오는 효과(카드 뒤집기 등)를 시작한다.
 */
export const ScreenReady = createContext(true)
