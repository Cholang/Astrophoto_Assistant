import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { closeApp, onCloseRequest } from '../host'
import { PRODUCT } from '../product'
import ConfirmDialog from './ConfirmDialog'

/**
 * 아이라 종료 (2026-10-10 레일에서 분리 — 종료는 앱 전체의 일이라 레일을 숨기거나 빼도 그대로 동작해야 한다).
 * 확인 창 → 포커서를 0으로 → 장비 연결 중이면 멈추고 끊기 → 아이라가 켠 N.I.N.A.·PHD2 닫기 → 창 닫기.
 * 레일의 "아이라 종료" 버튼과 창의 × 버튼이 같은 확인을 쓴다(`useQuit`)
 */
const QuitContext = createContext<() => void>(() => closeApp())

/** 종료 확인을 띄운다 (QuitProvider 밖에서는 바로 닫음) */
export const useQuit = () => useContext(QuitContext)

export function QuitProvider({
  stage,
  confirmOnClose,
  children,
}: {
  /** 지금 단계 이름 (확인 문구 "○○ 진행을 종료하고…"). 단계 전 화면이면 null */
  stage: string | null
  /** 창 ×도 확인을 거칠지 (진행 중일 때 — 단계 전 화면·마지막 요약은 바로 닫음) */
  confirmOnClose: boolean
  children: ReactNode
}) {
  const [quitting, setQuitting] = useState(false)
  // 종료 확인 뒤: 포커서를 0으로 되돌리는 중(focuser) → 장비 연결 중이면 멈추고 끊는 중(busy) → 아이라가 켠 N.I.N.A.·PHD2 닫는 중(programs).
  // 끊지 못한 장비가 있으면 그 안내(notOff)
  const [closing, setClosing] = useState<{ busy: boolean; notOff: string[]; step?: 'focuser' | 'programs' } | null>(null)

  const quit = async () => {
    if (closing && !closing.busy) return closeApp() // 끊지 못한 장비가 있어도 "그래도 종료" (N.I.N.A.는 남겨 둠 — 사용자가 직접 확인)
    // 포커서를 0(노브 끝까지 넣은 위치)에 두고 끝낸다 — 다음에 노브를 손으로 맞추지 않게 (2026-10-09). 못 하면(촬영·작업 중, 미연결) 그냥 종료
    setClosing({ busy: true, notOff: [], step: 'focuser' })
    await fetch('/api/focuser/park', { method: 'POST' }).catch(() => null)
    setClosing({ busy: true, notOff: [] })
    const notOff = await fetch('/api/equipment/abort', { method: 'POST' })
      .then((r) => (r.ok ? (r.json() as Promise<{ notDisconnected: string[] }>) : null))
      .then((b) => b?.notDisconnected ?? [])
      .catch(() => [] as string[])
    if (notOff.length > 0) return setClosing({ busy: false, notOff })
    // 아이라가 켠 N.I.N.A.면 PHD2와 함께 닫는다 — 남겨 두면 아이라 없이 N.I.N.A. 알림만 뜬다 (2026-10-09). 사용자가 켜 둔 N.I.N.A.는 그대로
    setClosing({ busy: true, notOff: [], step: 'programs' })
    await fetch('/api/engine/close', { method: 'POST' }).catch(() => null)
    closeApp()
  }

  // 창의 × 버튼도 같은 확인
  useEffect(() => {
    if (!confirmOnClose) return
    onCloseRequest(() => setQuitting(true))
    return () => onCloseRequest(null)
  }, [confirmOnClose])

  return (
    <QuitContext.Provider value={() => setQuitting(true)}>
      {children}
      <ConfirmDialog
        open={quitting}
        message={
          closing?.busy
            ? closing.step === 'focuser'
              ? '포커서를 0으로 되돌리는 중입니다…'
              : closing.step === 'programs'
                ? 'N.I.N.A.와 PHD2를 닫는 중입니다…'
                : '장비 연결을 멈추고 연결을 끊는 중입니다…'
            : closing
              ? `${closing.notOff.join(', ')} 연결을 끊지 못했습니다. N.I.N.A.에서 직접 끊어 주세요.`
              : stage
                ? `${stage} 진행을 종료하고 ${PRODUCT.reul} 종료합니다.`
                : `${PRODUCT.reul} 종료합니다.`
        }
        confirmLabel={closing && !closing.busy ? '그래도 종료' : '종료'}
        busy={closing?.busy}
        onConfirm={() => void quit()}
        onCancel={() => {
          setQuitting(false)
          setClosing(null)
        }}
      />
    </QuitContext.Provider>
  )
}
