import { Pencil } from 'lucide-react'
import ProfileAvatar from '../components/ProfileAvatar'
import { profileImageUrl, type Profile } from '../profiles'
import styles from './ProfileScreen.module.css'

/**
 * 프로필 선택 (넷플릭스·유튜브 프로필 화면 방식).
 * 화면 가운데에 첫 칸 "새 프로필", 그 오른쪽으로 만든 순서대로 놓는다.
 * 프로필이 하나여도 항상 보여 주고, 마지막에 쓴 프로필에 초점을 둔다 (Enter 한 번으로 시작).
 */
export default function ProfileScreen({
  profiles,
  onSelect,
  onNew,
  onEdit,
}: {
  profiles: Profile[]
  onSelect: (p: Profile) => void
  onNew: () => void
  /** 프로필 편집 (별명·메모·사진). 타일을 가리키면 연필이 보인다 — 편집은 앱 시작 때만 (DESIGN.md 3장) */
  onEdit: (p: Profile) => void
}) {
  const lastUsed = profiles.reduce<Profile | null>(
    (best, p) => (!best || (p.lastUsedAt ?? '') > (best.lastUsedAt ?? '') ? p : best),
    null,
  )

  return (
    <main className={styles.stage}>
      <div className={styles.content}>
        <h1 className={styles.title}>프로필 선택</h1>

        <ul className={styles.grid}>
          <li>
            <button type="button" className={styles.tile} onClick={onNew}>
              <span className={styles.add} aria-hidden="true">
                +
              </span>
              <span className={styles.name}>새 프로필</span>
            </button>
          </li>
          {profiles.map((p) => (
            <li key={p.id} className={styles.slot}>
              <button type="button" className={styles.tile} onClick={() => onSelect(p)} autoFocus={p.id === lastUsed?.id}>
                <ProfileAvatar src={p.hasImage ? profileImageUrl(p) : null} size="large" />
                <span className={styles.name}>{p.nickname}</span>
                {p.memo && <span className={styles.memo}>{p.memo}</span>}
              </button>
              <button type="button" className={styles.edit} aria-label={`${p.nickname} 프로필 편집`} title="프로필 편집" onClick={() => onEdit(p)}>
                <Pencil strokeWidth={2} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      </div>
    </main>
  )
}
