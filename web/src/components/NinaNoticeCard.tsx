import { useEffect, useRef, useState } from 'react'
import Button from './Button'
import DiagnosticCard from './DiagnosticCard'

interface NinaNotice {
  id: number
  at: string
  category: string
  title: string
  why: string[]
  fix: string
  raw: string
  count: number
}

/**
 * N.I.N.A. 오류를 아이라 말로 (2026-10-09 사용자 요청). 서버가 N.I.N.A. 로그를 읽어 풀어 쓴 알림을 2초마다 받아 진단 카드로 보인다.
 * N.I.N.A.의 알림 창은 아이라가 앞에 있을 때 서버가 숨긴다.
 * hide: 지금 화면이 스스로 보여 주는 종류(예: 장비 연결 화면의 연결 실패)는 겹쳐 띄우지 않는다
 */
export default function NinaNoticeCard({ hide, paused }: { hide: string[]; paused: boolean }) {
  const [queue, setQueue] = useState<NinaNotice[]>([])
  const last = useRef(0)
  // 받을 때 지금 화면이 다루는 종류는 버린다 (나중에 다른 화면으로 가서 뒤늦게 뜨지 않게)
  const hidden = useRef(hide)
  useEffect(() => {
    hidden.current = hide
  }, [hide])

  useEffect(() => {
    let alive = true
    const poll = async () => {
      try {
        const r = await fetch(`/api/nina/notices?after=${last.current}`)
        if (r.ok) {
          const list = (await r.json()) as NinaNotice[]
          if (alive && list.length) {
            last.current = Math.max(last.current, ...list.map((n) => n.id))
            const keep = list.filter((n) => !hidden.current.includes(n.category))
            // 같은 제목은 갱신(반복 횟수) — 서버가 반복 알림을 30초에 한 번 새 번호로 다시 보낸다
            if (keep.length) setQueue((q) => [...q.filter((o) => !keep.some((k) => k.title === o.title)), ...keep].slice(-20))
          }
        }
      } catch {
        // 서버가 잠깐 응답하지 않음 — 다음에 다시
      }
    }
    void poll()
    const timer = window.setInterval(() => void poll(), 2000)
    return () => {
      alive = false
      window.clearInterval(timer)
    }
  }, [])

  const shown = queue.filter((n) => !hide.includes(n.category))
  const top = shown.at(-1)
  if (!top || paused) return null
  const more = shown.length - 1
  return (
    <DiagnosticCard
      tone="warn"
      title={top.title}
      note={more > 0 ? `외 ${more}건` : top.count > 1 ? `${top.count}번` : undefined}
      why={top.why}
      fix={top.fix}
      raw={top.raw}
      actions={
        <Button onClick={() => setQueue((q) => q.filter((n) => n.id !== top.id))}>{more > 0 ? '다음' : '닫기'}</Button>
      }
    />
  )
}
