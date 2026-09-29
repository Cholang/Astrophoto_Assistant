import styles from './ProfileAvatar.module.css'

/**
 * 정사각형 프로필 이미지. 이미지를 넣지 않았으면 기본 아바타(사람 모양).
 * object-fit: cover로 비율을 지키고 넘치는 부분만 잘라 왜곡이 없게 한다.
 * 기본 아바타는 색 토큰으로 그려서 촬영용 적색 테마에서도 빨강으로만 보인다.
 */
export default function ProfileAvatar({ src, size }: { src?: string | null; size: 'small' | 'large' | 'hero' }) {
  return (
    <span className={styles.avatar} data-size={size} aria-hidden="true">
      {src ? (
        <img src={src} alt="" />
      ) : (
        <svg className={styles.fallback} viewBox="0 0 64 64">
          <circle cx="32" cy="24" r="12" />
          <path d="M8 64c0-14 10.7-23 24-23s24 9 24 23z" />
        </svg>
      )}
    </span>
  )
}
