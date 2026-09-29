import ProfileAvatar from '../components/ProfileAvatar'
import { profileImageUrl, type Profile } from '../profiles'
import styles from './ProfileScreen.module.css'

/**
 * 프로필 선택 (넷플릭스·유튜브 프로필 화면 방식).
 * 프로필이 하나여도 항상 보여 주고, 마지막에 쓴 프로필에 초점을 둔다 (Enter 한 번으로 시작).
 */
export default function ProfileScreen({
  profiles,
  onSelect,
  onNew,
}: {
  profiles: Profile[]
  onSelect: (p: Profile) => void
  onNew: () => void
}) {
  return (
    <main className={styles.stage}>
      <div className={styles.content}>
        <div className={styles.intro}>
          <h1>누구로 시작할까요?</h1>
          <p>프로필을 고르면 이 PC의 촬영 준비 상태를 확인합니다.</p>
        </div>

        <ul className={styles.grid}>
          {profiles.map((p, i) => (
            <li key={p.id}>
              {/* 목록은 마지막 사용 순이라 첫 칸이 마지막에 쓴 프로필 */}
              <button type="button" className={styles.tile} onClick={() => onSelect(p)} autoFocus={i === 0}>
                <ProfileAvatar src={p.hasImage ? profileImageUrl(p) : null} name={p.nickname} size="large" />
                <span className={styles.name}>{p.nickname}</span>
                {p.memo && <span className={styles.memo}>{p.memo}</span>}
              </button>
            </li>
          ))}
          <li>
            <button type="button" className={styles.tile} onClick={onNew}>
              <span className={styles.add} aria-hidden="true">
                +
              </span>
              <span className={styles.name}>새 프로필</span>
            </button>
          </li>
        </ul>
      </div>
    </main>
  )
}
