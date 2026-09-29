import { useId, useState } from 'react'
import styles from './InfoTip.module.css'

/**
 * 원 안의 물음표 아이콘 + 툴팁. 마우스를 올리거나, 키보드로 초점을 옮기거나, 눌러서 연다.
 * 용어 설명, 해결 방법처럼 "필요할 때만 보는 한두 문장"에 쓴다.
 */
export default function InfoTip({ label, text }: { label: string; text: string }) {
  const [open, setOpen] = useState(false)
  const id = useId()

  return (
    <span className={styles.anchor} onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
      <button
        type="button"
        className={styles.q}
        aria-label={label}
        aria-describedby={open ? id : undefined}
        aria-expanded={open}
        // 마우스를 올리면 이미 열려 있으므로 누르면 닫히지 않게 "열기"만 한다. 닫기는 Esc·초점 이동·마우스 떠남
        onClick={() => setOpen(true)}
        onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
      >
        ?
      </button>
      {open && (
        <span role="tooltip" id={id} className={styles.tip}>
          {text}
        </span>
      )}
    </span>
  )
}
