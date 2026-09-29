import { useEffect, useState } from 'react'
import { PRODUCT } from '../product'
import { profileImageUrl, type Profile } from '../profiles'
import type { Theme } from '../theme'
import ProfileAvatar from './ProfileAvatar'
import styles from './StatusBar.module.css'
import ThemeSwitch from './ThemeSwitch'

export interface DeviceState {
  name: string
  connected: boolean
}

/**
 * 상단 상태 줄 (DESIGN.md 3장): 프로필, 장비 연결 점, 시각, 화면 모드.
 * 평소에는 조용하고, 문제가 생길 때만 색이 바뀐다.
 */
export default function StatusBar({
  profile,
  devices,
  label,
  theme,
  onThemeChange,
}: {
  profile: Profile | null
  devices: DeviceState[] | null
  /** 장비 연결 전에 점 대신 보여 줄 지금 단계 이름 */
  label: string
  theme: Theme
  onThemeChange: (t: Theme) => void
}) {
  const now = useClock()

  return (
    <header className={styles.bar}>
      <span className={styles.brand}>{PRODUCT.name}</span>

      {profile && (
        <span className={styles.profile}>
          <ProfileAvatar src={profile.hasImage ? profileImageUrl(profile) : null} size="small" />
          {profile.nickname}
        </span>
      )}

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

      <time className={styles.clock} dateTime={now.toISOString()}>
        {now.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false })}
      </time>

      <ThemeSwitch value={theme} onChange={onThemeChange} />
    </header>
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
