import { MapPin, Save, Settings2, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { addSite, removeSite, SITES_MAX, type ObservingSite, type Profile } from '../profiles'
import { coordValue, sameSpot, type CurrentSite } from '../sites'
import styles from './SiteMenu.module.css'
import UndoToast from './UndoToast'

/**
 * 상태 줄의 관측지 목록 (연필을 누르면 바로 아래에 펼쳐짐, DESIGN.md 3장 "관측지").
 * - 누르면 그 관측지로 바로 바꾼다 (onPick → 적용). 지금 관측지 왼쪽에만 핀 아이콘
 * - 휴지통: 바로 지우고 "되돌리기"를 잠깐. 지금 관측지는 지울 수 없다
 * - 지금 관측지가 이 프로필에 없으면(프로필을 바꾼 경우) 맨 아래 점선 칸 + 저장(디스크). 10개면 저장 비활성
 * - 맨 아래 "관측지 편집" → 관측지 고르기 화면 (추가는 거기서)
 */
export default function SiteMenu({
  profile,
  current,
  tempName,
  onProfileChange,
  onPick,
  onEdit,
  onClose,
}: {
  profile: Profile
  current: CurrentSite | null
  /** 저장되지 않은 지금 관측지의 이름 (다른 프로필에 저장된 이름). 없으면 좌표 */
  tempName: string | null
  onProfileChange: (p: Profile) => void
  onPick: (s: ObservingSite) => void
  onEdit: () => void
  onClose: () => void
}) {
  const box = useRef<HTMLDivElement>(null)
  const sites = profile.sites ?? []
  const currentSaved = current ? sites.find((s) => sameSpot(s, current)) ?? null : null
  const [undo, setUndo] = useState<ObservingSite | null>(null)
  const [error, setError] = useState<string | null>(null)

  // 바깥을 누르거나 Esc면 닫는다
  useEffect(() => {
    const down = (e: PointerEvent) => {
      if (box.current && !box.current.contains(e.target as Node) && !(e.target as HTMLElement).closest('[data-site-toggle]')) onClose()
    }
    const key = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('pointerdown', down)
    document.addEventListener('keydown', key)
    return () => {
      document.removeEventListener('pointerdown', down)
      document.removeEventListener('keydown', key)
    }
  }, [onClose])

  const run = async (job: () => Promise<Profile>) => {
    setError(null)
    try {
      onProfileChange(await job())
      return true
    } catch (e) {
      setError((e as Error).message)
      return false
    }
  }

  const remove = async (s: ObservingSite) => {
    if (await run(() => removeSite(profile.id, s.id))) setUndo(s)
  }
  const restore = async () => {
    const s = undo
    setUndo(null)
    if (s) await run(() => addSite(profile.id, s))
  }
  const expire = useCallback(() => setUndo(null), [])

  const saveTemp = () => current && run(() => addSite(profile.id, { name: tempName ?? coordValue(current), ...current }))

  return (
    <div ref={box} className={styles.menu} role="dialog" aria-label="관측지 고르기">
      <ul className={styles.list}>
        {sites.map((s) => {
          const isCurrent = s.id === currentSaved?.id
          return (
            <li key={s.id} className={styles.row} data-current={isCurrent}>
              <button type="button" className={styles.pick} onClick={() => !isCurrent && onPick(s)} aria-current={isCurrent || undefined}>
                <span className={styles.pin}>{isCurrent && <MapPin strokeWidth={2} aria-label="지금 관측지" />}</span>
                <span className={styles.text}>
                  <span className={styles.name}>{s.name}</span>
                  <span className={styles.coord}>{coordValue(s)}</span>
                </span>
              </button>
              <button
                type="button"
                className={styles.icon}
                aria-label={`${s.name} 지우기`}
                title={isCurrent ? '지금 관측지는 지울 수 없습니다' : '지우기'}
                disabled={isCurrent}
                onClick={() => remove(s)}
              >
                <Trash2 strokeWidth={2} aria-hidden="true" />
              </button>
            </li>
          )
        })}
        {/* 저장되지 않은 지금 관측지: 점선 칸 (임시 값) */}
        {current && !currentSaved && (
          <li className={styles.row} data-current="true" data-temp="true">
            <span className={styles.pick}>
              <span className={styles.pin}>
                <MapPin strokeWidth={2} aria-label="지금 관측지" />
              </span>
              <span className={styles.text}>
                <span className={styles.name}>{tempName ?? '이름 없는 관측지'}</span>
                <span className={styles.coord}>{coordValue(current)}</span>
              </span>
            </span>
            <button
              type="button"
              className={styles.icon}
              aria-label="이 프로필에 저장"
              title={sites.length >= SITES_MAX ? `관측지는 ${SITES_MAX}개까지 저장할 수 있습니다. 하나를 지운 뒤 저장해 주세요` : '이 프로필에 저장'}
              disabled={sites.length >= SITES_MAX}
              onClick={saveTemp}
            >
              <Save strokeWidth={2} aria-hidden="true" />
            </button>
          </li>
        )}
        {sites.length === 0 && !current && <li className={styles.empty}>저장된 관측지가 없습니다</li>}
      </ul>
      <div className={styles.foot}>
        {undo ? (
          <UndoToast message={`${undo.name} 관측지를 지웠습니다`} onUndo={restore} onExpire={expire} />
        ) : error ? (
          <span className={styles.error}>{error}</span>
        ) : (
          <button type="button" className={styles.edit} onClick={onEdit}>
            <Settings2 strokeWidth={2} aria-hidden="true" />
            관측지 편집
          </button>
        )}
      </div>
    </div>
  )
}
