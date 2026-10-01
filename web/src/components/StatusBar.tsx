import { Pencil } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { PRODUCT, VERSION } from '../product'
import { profileImageUrl, type ObservingSite, type Profile } from '../profiles'
import { coordText, type CurrentSite } from '../sites'
import type { Theme } from '../theme'
import ProfileAvatar from './ProfileAvatar'
import SiteMenu from './SiteMenu'
import styles from './StatusBar.module.css'
import ThemeSwitch from './ThemeSwitch'

export interface DeviceState {
  name: string
  connected: boolean
}

/** 상태 줄의 관측지 (장비 연결 이후 화면에서만) */
export interface SiteBar {
  current: CurrentSite | null
  /** 이 프로필에 저장된 이름. 없으면 null */
  name: string | null
  /** 이 프로필에는 없고 다른 프로필에 저장된 이름 → "(저장 안 됨)" */
  otherName: string | null
  /** 적용 중이면 그 단계 문장 */
  syncing: string | null
  /** 연필을 보여 줄지 (관측지 고르기 화면에서는 숨김) */
  editable: boolean
}

/**
 * 상단 상태 줄 (DESIGN.md 3장): 프로필, 장비 연결 점, 관측지, 시각, 화면 모드.
 * 평소에는 조용하고, 문제가 생길 때만 색이 바뀐다.
 */
export default function StatusBar({
  profile,
  profiles,
  onSwitchProfile,
  onProfileChange,
  devices,
  label,
  site,
  onPickSite,
  onEditSites,
  theme,
  onThemeChange,
}: {
  profile: Profile | null
  profiles: Profile[]
  onSwitchProfile: (p: Profile) => void
  onProfileChange: (p: Profile) => void
  devices: DeviceState[] | null
  /** 장비 연결 전에 점 대신 보여 줄 지금 단계 이름 */
  label: string
  site: SiteBar | null
  onPickSite: (s: ObservingSite) => void
  onEditSites: () => void
  theme: Theme
  onThemeChange: (t: Theme) => void
}) {
  const now = useClock()
  const [siteOpen, setSiteOpen] = useState(false)

  return (
    <header className={styles.bar}>
      <span className={styles.brand}>{PRODUCT.name}</span>
      <span className={styles.version}>{VERSION}</span>

      {profile && <ProfileSwitcher profile={profile} profiles={profiles} onSwitch={onSwitchProfile} />}

      <span className={styles.devices}>
        {devices === null ? (
          <span className={styles.muted}>{label}</span>
        ) : (
          devices.map((d) => (
            <span key={d.name} className={styles.device} data-connected={d.connected}>
              <span className={styles.dot} aria-hidden="true" />
              {d.name}
              <span className={styles.srOnly}>{d.connected ? ' 연결됨' : ' 연결 안 됨'}</span>
            </span>
          ))
        )}
      </span>

      {site && profile && (
        <span className={styles.site} data-open={siteOpen}>
          {site.syncing ? (
            <span className={styles.syncing}>{site.syncing}</span>
          ) : site.current ? (
            <>
              {site.name && <span className={styles.siteName}>{site.name}</span>}
              {!site.name && site.otherName && (
                <span className={styles.siteName} title="이 프로필에 저장되지 않은 장소입니다">
                  <span className={styles.unsaved}>(저장 안 됨)</span> {site.otherName}
                </span>
              )}
              <span className={styles.coord}>{coordText(site.current)}</span>
            </>
          ) : (
            <span className={styles.noSite}>관측지 없음</span>
          )}
          {site.editable && (
            <button
              type="button"
              className={styles.pen}
              data-site-toggle=""
              aria-label="관측지 고르기"
              aria-expanded={siteOpen}
              disabled={!!site.syncing}
              onClick={() => setSiteOpen((o) => !o)}
            >
              <Pencil strokeWidth={2} aria-hidden="true" />
            </button>
          )}
          {siteOpen && site.editable && (
            <SiteMenu
              profile={profile}
              current={site.current}
              tempName={site.otherName}
              onProfileChange={onProfileChange}
              onPick={(s) => {
                setSiteOpen(false)
                onPickSite(s)
              }}
              onEdit={() => {
                setSiteOpen(false)
                onEditSites()
              }}
              onClose={() => setSiteOpen(false)}
            />
          )}
        </span>
      )}

      <time className={styles.clock} dateTime={now.toISOString()}>
        {now.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false })}
      </time>

      <ThemeSwitch value={theme} onChange={onThemeChange} />
    </header>
  )
}

/**
 * 프로필 바꾸기 (DESIGN.md 3장 "관측지"·"프로필"): 프로필 사진을 누르면 다른 프로필 사진들이
 * 바로 아래에서 같은 크기로 오른쪽으로 펼쳐지고, 가리키면 별명만 보인다. 누르면 바로 그 프로필로.
 * 프로필이 하나뿐이면 강조도 반응도 없다. 편집은 앱 시작 때 프로필 선택 화면에서.
 */
function ProfileSwitcher({ profile, profiles, onSwitch }: { profile: Profile; profiles: Profile[]; onSwitch: (p: Profile) => void }) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLSpanElement>(null)
  const others = profiles.filter((p) => p.id !== profile.id)
  const canSwitch = others.length > 0

  useEffect(() => {
    if (!open) return
    const down = (e: PointerEvent) => box.current && !box.current.contains(e.target as Node) && setOpen(false)
    const key = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('pointerdown', down)
    document.addEventListener('keydown', key)
    return () => {
      document.removeEventListener('pointerdown', down)
      document.removeEventListener('keydown', key)
    }
  }, [open])

  return (
    <span ref={box} className={styles.profile}>
      <button
        type="button"
        className={styles.me}
        data-can={canSwitch}
        disabled={!canSwitch}
        aria-label={canSwitch ? `${profile.nickname} · 프로필 바꾸기` : profile.nickname}
        aria-expanded={canSwitch ? open : undefined}
        onClick={() => setOpen((o) => !o)}
      >
        <ProfileAvatar src={profile.hasImage ? profileImageUrl(profile) : null} size="small" />
      </button>
      {profile.nickname}
      {open && (
        <span className={styles.others} role="menu">
          {others.map((p) => (
            <button
              key={p.id}
              type="button"
              role="menuitem"
              className={styles.other}
              aria-label={`${p.nickname}(으)로 바꾸기`}
              onClick={() => {
                setOpen(false)
                onSwitch(p)
              }}
            >
              <ProfileAvatar src={p.hasImage ? profileImageUrl(p) : null} size="small" />
              <span className={styles.tip}>{p.nickname}</span>
            </button>
          ))}
        </span>
      )}
    </span>
  )
}

function useClock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 15_000)
    return () => clearInterval(t)
  }, [])
  return now
}
