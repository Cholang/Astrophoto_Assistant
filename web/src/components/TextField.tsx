import type { InputHTMLAttributes } from 'react'
import styles from './TextField.module.css'

/** 이름표 + 입력창 + (있으면) 글자 수. 선택 항목은 이름표 옆에 "선택"을 붙인다. */
export default function TextField({
  id,
  label,
  optional,
  maxLength,
  value,
  ...rest
}: InputHTMLAttributes<HTMLInputElement> & { id: string; label: string; optional?: boolean; value: string }) {
  return (
    <div className={styles.field}>
      <div className={styles.labelLine}>
        <label htmlFor={id}>
          {label}
          {optional && <span className={styles.optional}>선택</span>}
        </label>
        {maxLength && (
          <span className={styles.count} aria-hidden="true">
            {value.length} / {maxLength}
          </span>
        )}
      </div>
      <input id={id} className={styles.input} maxLength={maxLength} value={value} {...rest} />
    </div>
  )
}
