import { ChevronLeft, MapPin, Plus, Trash2 } from 'lucide-react'
import { useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { ScreenReady } from '../screenReady'
import Button from '../components/Button'
import JarvisRing, { type RingState } from '../components/JarvisRing'
import KakaoMap, { type Spot } from '../components/KakaoMap'
import TextField from '../components/TextField'
import UndoToast from '../components/UndoToast'
import { addSite, removeSite, renameSite, SITE_NAME_MAX_BYTES, SITES_MAX, type ObservingSite, type Profile } from '../profiles'
import { applySite, coordValue, elevationOf, parseCoords, sameSpot, type CurrentSite } from '../sites'
import styles from './SiteScreen.module.css'

/**
 * 관측지 고르기 (DESIGN.md 3장 "관측지"): 장비 연결 뒤 "변경"을 눌렀거나 관측지가 없을 때, 또는 상태 줄 목록의 "관측지 편집".
 * 위: [뒤로] + 카드 5×2("관측지 추가"가 맨 앞 — 10개면 없음). 아래: 고른 관측지의 지도와 정보(이름은 바로 고칠 수 있음), 추가할 때는 입력칸.
 * 카드를 누르면 뒤집히며 "한 번 더 누르면 …" 안내 → 한 번 더 누르면 N.I.N.A.에 저장하고 적도의에 위치를 보낸 뒤(원형 표시) 돌아갈 곳으로.
 * 지금 관측지 카드를 한 번 더 누르면 동기화 없이 그대로 계속. 뒤로 화살표는 아무것도 바꾸지 않고 나간다(관측지가 없으면 숨김).
 * 이 화면은 프로필의 관측지만 다룬다 — 저장되지 않은 지금 관측지는 카드로 보이지 않는다 (임시 값이 저장된 것처럼 보이지 않게).
 */
export default function SiteScreen({
  profile,
  current,
  onProfileChange,
  onApplied,
  onDone,
}: {
  profile: Profile
  current: CurrentSite | null
  onProfileChange: (p: Profile) => void
  /** 적용이 끝나면 새 관측지 */
  onApplied: (s: CurrentSite) => void
  /** 이 화면을 나간다 (적용했든 뒤로 갔든) */
  onDone: () => void
}) {
  const sites = useMemo(() => profile.sites ?? [], [profile.sites])
  const currentId = current ? sites.find((s) => sameSpot(s, current))?.id ?? null : null
  const [selected, setSelected] = useState<string | null>(currentId)
  const [adding, setAdding] = useState(sites.length === 0)
  const [undo, setUndo] = useState<{ site: ObservingSite } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [apply, setApply] = useState<{ state: RingState; text: string } | null>(null)

  const chosen = sites.find((s) => s.id === selected) ?? null
  // 화면 전환이 끝난 뒤에야 고른 카드를 뒤집고, 오른쪽 정보에 빛이 훑고 지나가게 한다
  const ready = useContext(ScreenReady)

  // 목록이 비면 바로 추가부터
  useEffect(() => {
    if (sites.length === 0) setAdding(true)
  }, [sites.length])

  const remove = async (site: ObservingSite) => {
    setError(null)
    try {
      onProfileChange(await removeSite(profile.id, site.id))
      if (selected === site.id) setSelected(null)
      setUndo({ site })
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const restore = async () => {
    if (!undo) return
    const { site } = undo
    setUndo(null)
    try {
      onProfileChange(await addSite(profile.id, site))
    } catch (e) {
      setError((e as Error).message)
    }
  }
  const expireUndo = useCallback(() => setUndo(null), [])

  const use = async (site: ObservingSite) => {
    // 이미 지금 관측지면 다시 보낼 것 없이 계속
    if (current && sameSpot(site, current)) return onDone()
    setApply({ state: 'working', text: '관측지를 저장하는 중입니다' })
    const result = await applySite(site, (text) => setApply({ state: 'working', text }))
    if (!result.ok) return setApply({ state: 'failed', text: result.message })
    onApplied({ latitude: site.latitude, longitude: site.longitude, elevation: site.elevation })
    setApply({ state: 'done', text: '관측지를 저장했습니다' })
    setTimeout(onDone, 1100)
  }

  // 카드: 처음 누르면 고르고(뒤집혀 안내), 고른 카드를 한 번 더 누르면 그 관측지로
  const press = (site: ObservingSite) => {
    if (!adding && selected === site.id) return void use(site)
    setAdding(false)
    setSelected(site.id)
  }

  if (apply)
    return (
      <main className={styles.applying}>
        <JarvisRing state={apply.state} className={styles.ring}>
          {apply.text}
        </JarvisRing>
        <div className={styles.applyActions} data-shown={apply.state === 'failed'}>
          <Button onClick={() => setApply(null)}>돌아가기</Button>
          <Button variant="primary" onClick={() => chosen && use(chosen)}>
            다시 시도
          </Button>
        </div>
      </main>
    )

  return (
    <main className={styles.stage}>
      <div className={styles.intro}>
        {/* 뒤로: 제목 줄의 왼쪽(설명 줄은 제목 아래에 맞춤). 아무것도 바꾸지 않고 나간다.
            관측지가 없으면(강제로 들어옴) 나갈 수 없으니 숨기되 자리는 둔다 — 제목·카드가 옆으로 움직이지 않게 */}
        <button type="button" className={styles.back} onClick={onDone} aria-label="뒤로" title="뒤로" disabled={!current} data-hidden={!current}>
          <ChevronLeft strokeWidth={2} aria-hidden="true" />
        </button>
        <h1>{adding ? '새 관측지 추가' : '오늘 어디에서 촬영하나요?'}</h1>
        <p>{adding ? '지도에서 촬영 장소를 누르거나 검색해 주세요. 복사한 좌표를 붙여 넣어도 됩니다.' : '관측지를 두 번 누르면 N.I.N.A.와 적도의에 알려 줍니다.'}</p>
      </div>

      <div className={styles.cardArea}>
        {/* 개수: 목록 오른쪽 위 (출발 전 점검의 n/6과 같은 자리) */}
        <span className={styles.count}>
          관측지 {sites.length} / {SITES_MAX}
        </span>
        <ul className={styles.cards}>
          {sites.length < SITES_MAX && (
            <li>
              <button type="button" className={styles.addCard} data-on={adding} onClick={() => setAdding(true)}>
                <Plus strokeWidth={1.75} aria-hidden="true" />
                관측지 추가
              </button>
            </li>
          )}
          {sites.map((s) => {
            const flipped = ready && !adding && s.id === selected
            return (
              <li key={s.id} className={styles.cardSlot}>
                <button type="button" className={styles.card} data-flipped={flipped} onClick={() => press(s)} aria-label={flipped ? `${s.name}: ${hint(s.id === currentId)}` : s.name}>
                  <span className={styles.inner}>
                    <span className={styles.front}>
                      <span className={styles.cardName}>
                        {s.id === currentId && <MapPin strokeWidth={2} aria-label="지금 관측지" />}
                        {s.name}
                      </span>
                      <span className={styles.cardCoord}>{coordValue(s)}</span>
                      <span className={styles.cardElev}>고도 {Math.round(s.elevation).toLocaleString('ko-KR')} m</span>
                    </span>
                    <span className={styles.backFace} aria-hidden="true">
                      {hint(s.id === currentId)}
                    </span>
                  </span>
                </button>
                {/* 고른 카드는 지울 수 없다 */}
                <button
                  type="button"
                  className={styles.trash}
                  aria-label={`${s.name} 지우기`}
                  title={s.id === selected ? '고른 관측지는 지울 수 없습니다' : '지우기'}
                  disabled={s.id === selected}
                  data-hidden={flipped}
                  onClick={() => remove(s)}
                >
                  <Trash2 strokeWidth={2} aria-hidden="true" />
                </button>
              </li>
            )
          })}
        </ul>
      </div>

      <div className={styles.lower}>
        {adding ? (
          <AddSite
            profile={profile}
            onCancel={sites.length > 0 ? () => setAdding(false) : undefined}
            onAdded={(p, site) => {
              onProfileChange(p)
              setSelected(site.id)
              setAdding(false)
            }}
          />
        ) : (
          <>
            <KakaoMap spot={chosen} />
            <aside className={styles.side} data-shine={ready && !!chosen}>
              {chosen ? (
                <SiteInfo key={chosen.id} profile={profile} site={chosen} onProfileChange={onProfileChange} />
              ) : (
                <p className={styles.muted}>위에서 관측지를 골라 주세요.</p>
              )}
            </aside>
          </>
        )}
      </div>

      <div className={styles.foot}>
        <span className={styles.toastSlot}>{undo && <UndoToast message={`${undo.site.name} 관측지를 지웠습니다`} onUndo={restore} onExpire={expireUndo} />}</span>
        <span className={styles.error}>{error}</span>
      </div>
    </main>
  )
}

const hint = (isCurrent: boolean) => (isCurrent ? '한 번 더 누르면 이 관측지로 계속합니다' : '한 번 더 누르면 현재 관측지로 설정됩니다')

/**
 * 고른 관측지의 정보. 이름은 바로 고칠 수 있고, 고치면 아래에 "수정하기"(추가하기와 같은 자리).
 * 좌표는 고치지 않는다 (2026-10-01 사용자 결정). 다른 카드를 누르면 고치던 이름은 사라진다(key로 새로 만듦)
 */
function SiteInfo({ profile, site, onProfileChange }: { profile: Profile; site: ObservingSite; onProfileChange: (p: Profile) => void }) {
  const [name, setName] = useState(site.name)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const changed = name.trim() !== site.name && name.trim().length > 0

  const save = async () => {
    if (!changed || saving) return
    setSaving(true)
    setError(null)
    try {
      onProfileChange(await renameSite(profile.id, site.id, name))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <TextField id="site-rename" label="관측지 이름" maxBytes={SITE_NAME_MAX_BYTES} placeholder={`최대 ${SITE_NAME_MAX_BYTES}byte`} value={name} onValueChange={setName} autoComplete="off" />
      <div className={styles.pair}>
        <div className={styles.readField}>
          <span className={styles.readLabel}>위도</span>
          <span className={styles.readValue}>{site.latitude.toFixed(5)}</span>
        </div>
        <div className={styles.readField}>
          <span className={styles.readLabel}>경도</span>
          <span className={styles.readValue}>{site.longitude.toFixed(5)}</span>
        </div>
      </div>
      <div className={styles.readField}>
        <span className={styles.readLabel}>고도</span>
        <span className={styles.readValue}>{Math.round(site.elevation).toLocaleString('ko-KR')} m</span>
      </div>
      <p className={styles.error} role="alert">
        {error}
      </p>
      <div className={styles.sideActions} data-shown={changed}>
        <Button variant="primary" onClick={save} disabled={!changed || saving}>
          {saving ? '수정하는 중' : '수정하기'}
        </Button>
      </div>
    </>
  )
}

/** 관측지 추가: 왼쪽 지도(검색·현재 위치·누르기) + 오른쪽 이름·좌표(붙여넣기)·고도(자동) */
function AddSite({ profile, onCancel, onAdded }: { profile: Profile; onCancel?: () => void; onAdded: (p: Profile, site: ObservingSite) => void }) {
  const [name, setName] = useState('')
  const [lat, setLat] = useState('')
  const [lon, setLon] = useState('')
  const [spot, setSpot] = useState<Spot | null>(null)
  const [elevation, setElevation] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // 좌표가 정해지면 고도를 조회한다
  useEffect(() => {
    if (!spot) return setElevation(null)
    let alive = true
    setElevation(null)
    elevationOf(spot.latitude, spot.longitude).then((e) => alive && setElevation(e))
    return () => {
      alive = false
    }
  }, [spot])

  const fromMap = (s: Spot) => {
    setSpot(s)
    setLat(s.latitude.toFixed(5))
    setLon(s.longitude.toFixed(5))
    setError(null)
  }

  // 위도·경도 칸. 어느 칸이든 "37.6231, 128.7432"처럼 두 값을 붙여 넣으면 두 칸에 나눠 넣는다
  const typed = (which: 'lat' | 'lon') => (text: string) => {
    const pair = /[,\s°'"NSEW]/i.test(text.trim()) ? parseCoords(text) : null
    const nextLat = pair ? pair.latitude.toFixed(5) : which === 'lat' ? text : lat
    const nextLon = pair ? pair.longitude.toFixed(5) : which === 'lon' ? text : lon
    setLat(nextLat)
    setLon(nextLon)
    const la = Number(nextLat)
    const lo = Number(nextLon)
    const ok = nextLat.trim() !== '' && nextLon.trim() !== '' && Math.abs(la) <= 90 && Math.abs(lo) <= 180 && !(la === 0 && lo === 0)
    setSpot(ok ? { latitude: la, longitude: lo } : null)
    setError(nextLat.trim() && nextLon.trim() && !ok ? '위도는 -90~90, 경도는 -180~180 사이의 숫자로 입력해 주세요' : null)
  }

  const submit = async () => {
    if (!spot || !name.trim() || saving) return
    setSaving(true)
    setError(null)
    try {
      const p = await addSite(profile.id, { name, ...spot, elevation: elevation ?? undefined })
      const site = p.sites?.[p.sites.length - 1]
      if (site) onAdded(p, site)
    } catch (e) {
      setError((e as Error).message)
      setSaving(false)
    }
  }

  return (
    <>
      <KakaoMap spot={spot} editable locate onPick={fromMap} />
      <aside className={styles.side}>
        <TextField id="site-name" label="관측지 이름" maxBytes={SITE_NAME_MAX_BYTES} placeholder={`최대 ${SITE_NAME_MAX_BYTES}byte`} value={name} onValueChange={setName} autoComplete="off" />
        <div className={styles.pair}>
          <TextField id="site-lat" label="위도" maxBytes={60} placeholder="예: 37.62310" value={lat} onValueChange={typed('lat')} autoComplete="off" inputMode="decimal" />
          <TextField id="site-lon" label="경도" maxBytes={60} placeholder="예: 128.74320" value={lon} onValueChange={typed('lon')} autoComplete="off" inputMode="decimal" />
        </div>
        <div className={styles.readField}>
          <span className={styles.readLabel}>고도</span>
          <span className={styles.readValue}>{spot ? (elevation === null ? '조회 중' : `${Math.round(elevation).toLocaleString('ko-KR')} m`) : '—'}</span>
          <span className={styles.readNote}>위도·경도로 자동 조회</span>
        </div>
        <p className={styles.error} role="alert">
          {error}
        </p>
        <div className={styles.sideActions}>
          {onCancel && <Button onClick={onCancel}>취소</Button>}
          <Button variant="primary" onClick={submit} disabled={!spot || !name.trim() || saving}>
            {saving ? '추가하는 중' : '추가하기'}
          </Button>
        </div>
      </aside>
    </>
  )
}
