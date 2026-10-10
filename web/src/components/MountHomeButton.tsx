import { House, Lock } from 'lucide-react'
import { useEffect, useState } from 'react'
import Button from './Button'
import ConfirmDialog from './ConfirmDialog'

/**
 * 적도의 홈 (2026-10-09 사용자 요청, 2026-10-10 레일에서 분리 — 레일은 자리만 주고 이 기능을 모른다).
 * 서버가 지금 움직여도 되는지(촬영·적도의를 쓰는 작업·가이딩·미연결이면 안 됨)와 이유를 알려 준다 (2초마다).
 * 움직여도 되면 집 아이콘, 안 되면 자물쇠 아이콘 + 흐리게 — 아이콘만 봐도 구분되게. 누르면 한 번 묻고 홈으로 보낸 뒤 추적을 끈다
 */
export default function MountHomeButton() {
  const [state, setState] = useState<{ available: boolean; homing: boolean; reason: string | null } | null>(null)
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    const poll = () =>
      fetch('/api/mount/home')
        .then((r) => (r.ok ? r.json() : null))
        .then((s) => alive && setState(s))
        .catch(() => alive && setState(null))
    void poll()
    const t = window.setInterval(poll, 2000)
    return () => {
      alive = false
      window.clearInterval(t)
    }
  }, [])
  if (!state) return null
  const on = state.available
  const label = state.homing ? '홈으로 가는 중' : '적도의 홈'
  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        aria-disabled={!on}
        title={on ? '적도의를 홈 위치로 보내고 추적을 끕니다' : (state.reason ?? undefined)}
        aria-label={on ? '적도의 홈으로 보내기' : `적도의 홈 — ${state.reason ?? '지금은 쓸 수 없음'}`}
        onClick={() => on && setAsking(true)}
      >
        {on || state.homing ? <House strokeWidth={2} aria-hidden="true" /> : <Lock strokeWidth={2} aria-hidden="true" />}
        <span>{label}</span>
      </Button>
      <ConfirmDialog
        open={asking || error !== null}
        message={error ?? '적도의를 홈 위치로 보내고 추적을 끕니다. 경통 주변에 걸리는 것이 없는지 확인해 주세요.'}
        confirmLabel={error ? '확인' : '홈으로 보내기'}
        single={error !== null}
        onConfirm={() => {
          if (error) return setError(null)
          setAsking(false)
          void fetch('/api/mount/home', { method: 'POST' }).then(async (r) => {
            if (!r.ok) setError(((await r.json().catch(() => null)) as { error?: string } | null)?.error ?? '적도의를 홈으로 보내지 못했습니다')
          })
        }}
        onCancel={() => {
          setAsking(false)
          setError(null)
        }}
      />
    </>
  )
}
