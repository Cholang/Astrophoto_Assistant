import { useCallback, useEffect, useState } from 'react'
import type { DiagnosisData } from './components/DiagnosisCard'
import type { Status } from './components/StatusIcon'

/** 서버 Astro.Core.Setup.CheckResult와 같은 모양. 0단계·1단계가 같이 쓴다. */
export interface CheckItem {
  id: string
  title: string
  term: string | null
  hint: string | null
  severity: 'Required' | 'Recommended'
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
      setItems((prev) => prev.map((item) => (item.id === result.id ? result : item)))
    })
    // 서버가 다 보내고 연결을 닫으면 브라우저는 재연결을 시도한다. 여기서 끊는다.
    source.onerror = () => {
      source.close()
      setClosed(true)
    }
    return () => source.close()
  }, [url, run])

  const finals = items.filter((i) => isFinal(i.status)).length
  const done = finals === items.length || closed
  const interrupted = done && finals < items.length
  const failed = items.some((i) => i.severity === 'Required' && i.status !== 'Pass')

  return { items, done, interrupted, failed, allPass: done && !interrupted && !failed, restart, run }
}
