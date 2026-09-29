import styles from './ProfileAvatar.module.css'

/**
 * 정사각형 프로필 이미지. 이미지가 없으면 닉네임 첫 글자.
 * object-fit: cover로 비율을 지키고 넘치는 부분만 잘라 왜곡이 없게 한다.
 */
export default function ProfileAvatar({ src, name, size }: { src?: string | null; name: string; size: 'small' | 'large' }) {
  return (
    <span className={styles.avatar} data-size={size} aria-hidden="true">
      {src ? <img src={src} alt="" /> : <span className={styles.initial}>{Array.from(name.trim())[0] ?? '?'}</span>}
    </span>
  )
}
