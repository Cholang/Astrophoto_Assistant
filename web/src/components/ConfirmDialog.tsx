import { useEffect, useRef, type ReactNode } from 'react'
import Button from './Button'
import styles from './ConfirmDialog.module.css'

/**
 * 확인 창 (공통). 되돌릴 수 없는 일을 하기 전에 한 번 묻는다 (예: 진행 중에 AA 종료).
 * 브라우저 <dialog>(showModal)라 Esc·바깥 누름은 취소, 키보드 초점은 창 안에 머문다. 처음 초점은 안전한 쪽(취소)에.
 * 창 위에 창을 겹치지 않는다 (DESIGN.md 10장).
 * busy = 확인한 일을 하는 중 (버튼을 잠그고 닫히지 않음 — 예: 장비 연결을 끊고 종료).
 * children = 문장 아래 덧붙이는 내용 (예: 종료 중 닫는 프로그램 목록).
 * single = 버튼 하나(알림), dismissable=false면 Esc·바깥 누름으로 닫히지 않는다(고르지 않고 넘어가면 안 되는 질문 — 예: 이어서 할까요)
 */
export default function ConfirmDialog({
  open,
  message,
  confirmLabel,
  cancelLabel = '취소',
  onConfirm,
  onCancel,
  single = false,
  dismissable = true,
  busy = false,
  children,
}: {
  open: boolean
  message: string
  confirmLabel: string
  cancelLabel?: string
  onConfirm: () => void
  onCancel: () => void
  single?: boolean
  dismissable?: boolean
  busy?: boolean
  children?: ReactNode
}) {
  const canDismiss = dismissable && !busy
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
        if (canDismiss) onCancel()
      }}
      onClick={(e) => {
        // 바깥(배경) 누름 = 취소
        if (canDismiss && e.target === ref.current) onCancel()
      }}
    >
      <p className={styles.message} aria-live="polite">
        {message}
      </p>
      {children}
      <div className={styles.actions}>
        <Button variant="primary" onClick={onConfirm} disabled={busy}>
          {confirmLabel}
        </Button>
        {!single && (
          <Button ref={cancelRef} onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
        )}
      </div>
    </dialog>
  )
}
