import { useEffect } from 'react'
import styles from './UndoToast.module.css'

/** 지운 뒤 잠깐 보이는 "되돌리기" (DESIGN.md 3장 "관측지": 확인 창 대신). 시간이 지나면 onExpire */
export const UNDO_MS = 5000

export default function UndoToast({ message, onUndo, onExpire }: { message: string; onUndo: () => void; onExpire: () => void }) {
  useEffect(() => {
    const t = setTimeout(onExpire, UNDO_MS)
    return () => clearTimeout(t)
  }, [message, onExpire])
  return (
    <div className={styles.toast} role="status">
      <span>{message}</span>
      <button type="button" onClick={onUndo}>
        되돌리기
      </button>
    </div>
  )
}
