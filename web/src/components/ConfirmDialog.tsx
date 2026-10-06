import { useEffect, useRef } from 'react'
import styles from './ConfirmDialog.module.css'

/**
 * 확인 창 (공통). 되돌릴 수 없는 일을 하기 전에 한 번 묻는다 (예: 진행 중에 AA 종료).
 * 브라우저 <dialog>(showModal)라 Esc·바깥 누름은 취소, 키보드 초점은 창 안에 머문다. 처음 초점은 안전한 쪽(취소)에.
 * 창 위에 창을 겹치지 않는다 (DESIGN.md 10장).
 */
export default function ConfirmDialog({
  open,
  message,
  confirmLabel,
  cancelLabel = '취소',
  onConfirm,
  onCancel,
}: {
  open: boolean
  message: string
  confirmLabel: string
  cancelLabel?: string
  onConfirm: () => void
  onCancel: () => void
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) {
      d.showModal()
      cancelRef.current?.focus()
    } else if (!open && d.open) d.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      className={styles.dialog}
      onCancel={(e) => {
        e.preventDefault()
        onCancel()
      }}
      onClick={(e) => {
        // 바깥(배경) 누름 = 취소
        if (e.target === ref.current) onCancel()
      }}
    >
      <p className={styles.message}>{message}</p>
      <div className={styles.actions}>
        <button type="button" className={styles.primary} onClick={onConfirm}>
          {confirmLabel}
        </button>
        <button type="button" ref={cancelRef} className={styles.quiet} onClick={onCancel}>
          {cancelLabel}
        </button>
      </div>
    </dialog>
  )
}
