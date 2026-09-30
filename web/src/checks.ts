import { useCallback, useEffect, useState } from 'react'
import type { DiagnosisData } from './components/DiagnosisCard'
import type { Status } from './components/StatusIcon'

/** 서버 Astro.Core.Setup.CheckResult와 같은 모양. 0단계·1단계가 같이 쓴다. */
export interface CheckItem {
  id: string
  title: string
  term: string | null
  hint: string | null
  /** Required: 통과해야 넘어감 · Recommended: 실패하면 사용자가 '없이 진행'을 고를 수 있음(Warn) · Optional: 실패해도 경고만 */
  severity: 'Required' | 'Recommended' | 'Optional'
  status: Status
  message: string
  diagnosis: DiagnosisData | null
}

export const pending = (id: string, title: string, term: string | null = null): CheckItem => ({
  id,
  title,
  term,
  hint: null,
  severity: 'Required',
  status: 'Pending',
  message: '',
  diagnosis: null,
})

export const isFinal = (s: Status) => s !== 'Pending' && s !== 'Running'

/**
 * 서버가 보내는 확인 결과(Server-Sent Events)를 받아 목록을 채운다.
 * restart()로 처음부터 다시 확인한다.
 */
export function useCheckStream(url: string, initial: CheckItem[]) {
  const [items, setItems] = useState(initial)
  const [closed, setClosed] = useState(false)
  const [run, setRun] = useState(0)

  const restart = useCallback(() => {
    setItems(initial)
    setClosed(false)
    setRun((r) => r + 1)
  }, [initial])

  useEffect(() => {
    const source = new EventSource(url)
    source.addEventListener('check', (e) => {
      const result = JSON.parse((e as MessageEvent).data) as CheckItem
      // 미리 그린 칸에 없는 결과(예: 장비 목록을 못 읽음)는 뒤에 붙인다
      setItems((prev) => (prev.some((i) => i.id === result.id) ? prev.map((i) => (i.id === result.id ? result : i)) : [...prev, result]))
    })
    // 서버가 다 보내고 연결을 닫으면 브라우저는 재연결을 시도한다. 여기서 끊는다.
    source.onerror = () => {
      source.close()
      setClosed(true)
    }
    return () => source.close()
  }, [url, run])

  /** [임시] 화면 설계용: 항목 하나를 통과한 것으로 바꾼다 (0단계 "설치 완료하기" 버튼). */
  const markPassed = useCallback(
    (id: string, message: string) =>
      setItems((prev) => prev.map((i) => (i.id === id ? { ...i, status: 'Pass' as const, message, diagnosis: null } : i))),
    [],
  )

  /**
   * 한 항목만 다시 확인한다 (쉐브론 안의 새로고침). 서버가 그 항목의 결과 하나를 돌려준다.
   * quiet: 확인하는 동안 "확인 중"으로 바꾸지 않고, 결과가 그대로면(상태·문장이 같으면) 항목을 건드리지 않는다
   *        (빠른 재확인이 번쩍이며 화면을 흔들지 않게 — 0단계 설치 확인)
   */
  const recheckOne = useCallback(async (id: string, oneUrl: string, quiet = false) => {
    if (!quiet) setItems((prev) => prev.map((i) => (i.id === id ? { ...i, status: 'Running' as const, diagnosis: null } : i)))
    try {
      const res = await fetch(oneUrl)
      if (!res.ok) throw new Error()
      const result = (await res.json()) as CheckItem
      setItems((prev) =>
        prev.map((i) => (i.id !== id ? i : quiet && i.status === result.status && i.message === result.message ? i : result)),
      )
    } catch {
      setItems((prev) =>
        prev.map((i) => (i.id === id ? { ...i, status: 'Fail' as const, message: '다시 확인하지 못했습니다' } : i)),
      )
    }
  }, [])

  const finals = items.filter((i) => isFinal(i.status)).length
  const done = finals === items.length || closed
  const interrupted = done && finals < items.length
  // 등급별 통과: 필수는 Pass만, 준필수는 Pass 또는 '없이 진행'(Warn), 선택은 끝나기만 하면 된다
  const failed = items.some((i) =>
    i.severity === 'Required' ? i.status !== 'Pass' : i.severity === 'Recommended' ? !['Pass', 'Warn', 'Absent'].includes(i.status) : !isFinal(i.status),
  )
  const allPass = done && !interrupted && !failed && items.length > 0

  return { items, done, interrupted, failed, allPass, restart, run, markPassed, recheckOne, patch: setItems }
}
