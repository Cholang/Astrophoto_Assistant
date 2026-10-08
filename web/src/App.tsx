import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { useFrame } from './frame'
import { ScreenReady } from './screenReady'
import StatusBar, { type DeviceState } from './components/StatusBar'
import NinaLostCard, { type NinaState } from './components/NinaLostCard'
import StepRail, { RailProvider, type RailExtra, type Stage } from './components/StepRail'
import { listProfiles, selectProfile, type Profile } from './profiles'
import BootScreen from './screens/BootScreen'
import EngineStartScreen from './screens/EngineStartScreen'
import EquipmentScreen, { type EquipmentDone } from './screens/EquipmentScreen'
import NewProfileScreen from './screens/NewProfileScreen'
import PlanScreen from './screens/PlanScreen'
import PrepareScreen from './screens/PrepareScreen'
import ShootScreen from './screens/ShootScreen'
import SummaryScreen from './screens/SummaryScreen'
import { prepareState, type PrepGroup } from './prepare'
import PreflightScreen from './screens/PreflightScreen'
import ProfileScreen from './screens/ProfileScreen'
import SetupCheckScreen from './screens/SetupCheckScreen'
import SiteScreen from './screens/SiteScreen'
import { applySite, describeSite, fetchCurrentSite, type CurrentSite } from './sites'
import type { ObservingSite } from './profiles'
import { PRODUCT } from './product'
import { useTheme } from './theme'
import ConfirmDialog from './components/ConfirmDialog'
import { dismissSession, pendingSession, reportPhase, resumeSession, type PendingSession } from './session'
import styles from './App.module.css'

// flow.mmd ① 시작 · 연결:
// 부팅 로고 → 프로필 선택 (없으면 새 프로필 만들기) → 0단계 설치 확인 → 1단계 엔진 켜기 → 장비 연결
// → 출발 전 점검 → 장비 준비(rig) → 대상(계획 → target) → (다음: 촬영). DESIGN.md "단계 재구성" (2026-10-07)
type Phase = 'boot' | 'profiles' | 'newProfile' | 'check' | 'engine' | 'equipment' | 'site' | 'preflight' | 'rig' | 'plan' | 'target' | 'shoot' | 'wrap' | 'summary'

/** 화면 → 하단 단계 레일의 단계. 부팅·프로필 화면에는 레일이 없다 (DESIGN.md 2장) */
const STAGE_OF: Partial<Record<Phase, Stage>> = {
  check: '연결',
  engine: '연결',
  equipment: '연결',
  site: '연결',
  preflight: '점검',
  rig: '장비 준비',
  plan: '대상',
  target: '대상',
  shoot: '촬영',
  wrap: '마무리',
  summary: '마무리',
}

/** N.I.N.A.가 켜져 있어야 하는 화면 (1단계 엔진 켜기 이후). 여기서 N.I.N.A.가 꺼지면 알린다 */
const WATCHED: Phase[] = ['equipment', 'site', 'preflight', 'rig', 'plan', 'target', 'shoot', 'wrap']

/** 상태 줄에 관측지를 보여 주는 화면 (장비 연결 이후). 연필은 장비 연결·관측지 고르기 화면에서는 숨긴다 */
const SITE_SHOWN: Phase[] = ['equipment', 'site', 'preflight', 'rig', 'plan', 'target', 'shoot', 'wrap', 'summary']
const SITE_EDITABLE: Phase[] = ['preflight', 'rig', 'plan', 'target']

/** 상단 상태 줄의 지금 단계 이름 */
const LABEL_OF: Partial<Record<Phase, string>> = {
  equipment: '장비 연결',
  site: '관측지',
  preflight: '출발 전 점검',
  rig: '장비 준비',
  plan: '대상 · 계획',
  target: '대상',
  shoot: '촬영',
  wrap: '마무리',
  summary: '오늘 밤 요약',
}

/** 화면 전환 페이드 한쪽 시간. App.module.css의 .screen transition과 같은 값 */
const FADE_MS = 280

export default function App() {
  const { theme, setTheme } = useTheme()
  const [phase, setPhase] = useState<Phase>('boot')
  const [shown, setShown] = useState(true)
  const [profiles, setProfiles] = useState<Profile[] | null>(null)
  const [profile, setProfile] = useState<Profile | null>(null)
  const [loadError, setLoadError] = useState(false)
  // 프로필 선택 화면에서 고치는 중인 프로필 (새 프로필 만들기 화면을 편집으로 쓴다)
  const [editing, setEditing] = useState<Profile | null>(null)
  const fading = useRef(false)

  /** 지금 화면을 서서히 감추고, 다음 화면을 서서히 보여 준다. */
  // 전환 중에는 화면을 누를 수 없다 (DESIGN.md 9장). 새 화면이 다 나타난 뒤 true
  const [settled, setSettled] = useState(true)
  // 지금 화면이 진행 표시에 넘긴 작업 목록·버튼 (예: 준비의 7작업, 준비 중단)
  const [railExtra, setRailExtra] = useState<RailExtra | null>(null)
  const fadeTo = useCallback((next: Phase) => {
    if (fading.current) return
    fading.current = true
    setSettled(false)
    setShown(false)
    setTimeout(() => {
      setPhase(next)
      // 다음 화면이 투명한 상태로 한 번 그려진 뒤에 보이게 해야 페이드인이 된다
      requestAnimationFrame(() =>
        requestAnimationFrame(() => {
          setShown(true)
          fading.current = false
          setTimeout(() => setSettled(true), FADE_MS)
        }),
      )
    }, FADE_MS)
  }, [])

  useEffect(() => {
    listProfiles().then(setProfiles, () => setLoadError(true))
  }, [])

  // 부팅 로고를 잠깐 보여 준 뒤, 프로필이 없으면 바로 만들기 화면으로
  useEffect(() => {
    if (phase !== 'boot' || profiles === null) return
    const t = setTimeout(() => fadeTo(profiles.length === 0 ? 'newProfile' : 'profiles'), 1200)
    return () => clearTimeout(t)
  }, [phase, profiles, fadeTo])

  // 프로필을 고르면 설치 상태를 바로 확인한다.
  // 모두 설치돼 있으면 0단계 화면은 건너뛰고(누를 필요 없는 "다음"을 없앤다), 빠진 게 있으면 0단계로.
  // 0단계는 빠진 첫 항목이 선택된 상태로 시작한다. 확인에 실패하면 안전하게 0단계로.
  const start = useCallback(
    async (p: Profile) => {
      setProfile(p)
      let allInstalled = false
      try {
        const res = await fetch('/api/setup/status')
        const items = res.ok ? ((await res.json()) as { status: string }[]) : []
        allInstalled = items.length > 0 && items.every((i) => i.status === 'Pass')
      } catch {
        /* 0단계에서 다시 확인한다 */
      }
      fadeTo(allInstalled ? 'engine' : 'check')
    },
    [fadeTo],
  )

  const choose = useCallback(
    (p: Profile) => {
      void selectProfile(p.id)
      start(p)
    },
    [start],
  )

  // 만든 프로필은 선택 화면을 거치지 않고 바로 시작한다 (서버가 만들 때 마지막 사용으로 기록).
  const created = useCallback(
    (p: Profile) => {
      setProfiles((list) => [...(list ?? []), p])
      start(p)
    },
    [start],
  )

  const toEngine = useCallback(() => setPhase('engine'), [])
  const toEquipment = useCallback(() => setPhase('equipment'), [])
  // 장비 연결이 끝나면 상태 줄에 장비별 연결 점을 보여 준다
  const [devices, setDevices] = useState<DeviceState[] | null>(null)
  // N.I.N.A.가 꺼져 다시 켠 경우: 장비를 다시 연결한 뒤 원래 있던 화면으로 돌아간다 (점검을 다시 묻지 않는다)
  const resumeTo = useRef<Phase | null>(null)

  // ── 관측지 (DESIGN.md 3장 "관측지"): 지금 관측지 = N.I.N.A.에 설정된 좌표. undefined면 아직 모름 ─────────
  const [site, setSite] = useState<CurrentSite | null | undefined>(undefined)
  const [siteSyncing, setSiteSyncing] = useState<string | null>(null)
  // 관측지 고르기 화면이 끝나면 돌아갈 곳 (장비 연결 뒤 → 출발 전 점검, 상태 줄 "관측지 편집" → 원래 화면)
  const siteReturn = useRef<Phase>('preflight')
  useEffect(() => {
    // 장비 연결 화면에 들어올 때 읽어 둔다 (N.I.N.A.가 켜진 뒤)
    if (phase === 'equipment') fetchCurrentSite().then(setSite, () => setSite(null))
  }, [phase])

  // 장비 연결 다음 화면으로. "변경"을 눌렀거나 관측지가 없으면 관측지 고르기를 먼저 (없으면 건너뛸 수 없음)
  const goAfterEquipment = useCallback(
    (next: Phase, changeSite: boolean) => {
      if (changeSite || site === null) {
        siteReturn.current = next
        return fadeTo('site')
      }
      fadeTo(next)
    },
    [fadeTo, site],
  )

  // 지난 진행 기록 (AA가 중간에 꺼졌다 다시 켬 — 2026-10-08): 장비 연결 뒤에 묻는다
  const [pending, setPending] = useState<(PendingSession & { changeSite: boolean; error?: string; busy?: boolean }) | null>(null)

  const toNext: EquipmentDone = useCallback((items, opts) => {
    // 상태 줄에는 장비 이름(OnStep 등)을 쓴다. 칸의 큰 글씨는 장비 종류, 작은 글씨(term)가 장비 이름
    setDevices(items.filter((i) => i.status !== 'Absent').map((i) => ({ name: i.term ?? i.title, connected: i.status === 'Pass' })))
    const back = resumeTo.current
    resumeTo.current = null
    if (back) return goAfterEquipment(back, !!opts.changeSite)
    void pendingSession(profile?.id ?? null).then((p) => {
      if (p) setPending({ ...p, changeSite: !!opts.changeSite })
      else goAfterEquipment('preflight', !!opts.changeSite)
    })
  }, [goAfterEquipment, profile])

  // 이어서: 출발 전 점검은 건너뛴다 (앱만 꺼졌다 켠 경우 — 2026-10-08 사용자 결정). 대상·촬영 중이었으면 대상 단계(이동부터)
  const answerPending = useCallback(
    async (resume: boolean) => {
      const p = pending
      if (!p || p.busy) return
      if (resume && p.kind === 'resume') {
        // 장비를 멈추고 끝낸 작업을 다시 확인하는 동안 (몇 초)
        setPending({ ...p, busy: true, error: undefined })
        const result = await resumeSession()
        if ('phase' in result) {
          setPending(null)
          return goAfterEquipment(result.phase as Phase, p.changeSite)
        }
        return setPending({ ...p, busy: false, error: result.error }) // 장비를 멈추지 못함 → 이유와 함께 다시 묻기
      }
      setPending(null)
      dismissSession()
      goAfterEquipment('preflight', p.changeSite)
    },
    [pending, goAfterEquipment],
  )

  // 지금 단계를 서버에 알린다 (장비 준비 이후만 기록 — 요약에 닿으면 그 밤은 끝)
  useEffect(() => {
    reportPhase(phase, profile?.id ?? null)
  }, [phase, profile])

  const updateProfile = useCallback((p: Profile) => {
    setProfile(p)
    setProfiles((list) => list?.map((x) => (x.id === p.id ? p : x)) ?? list)
  }, [])

  // 상태 줄 목록에서 고르면 바로 적용 (문장은 상태 줄 관측지 자리에)
  const pickSite = useCallback(async (s: ObservingSite) => {
    setSiteSyncing('관측지를 저장하는 중입니다')
    const result = await applySite(s, setSiteSyncing)
    if (result.ok) {
      setSiteSyncing(null)
      setSite({ latitude: s.latitude, longitude: s.longitude, elevation: s.elevation })
    } else {
      // 실패 문장을 잠깐 보여 준다 (N.I.N.A. 저장은 됐어도 적도의에 못 보냈을 수 있어 지금 값을 다시 읽는다)
      setSiteSyncing(result.message)
      fetchCurrentSite().then(setSite, () => undefined)
      setTimeout(() => setSiteSyncing(null), 5000)
    }
  }, [])

  const editSites = useCallback(() => {
    siteReturn.current = phase
    fadeTo('site')
  }, [phase, fadeTo])

  // 프로필 바꾸기 (상태 줄 프로필 사진). 관측지는 지금 서 있는 장소라 그대로 둔다
  const switchProfile = useCallback((p: Profile) => {
    void selectProfile(p.id)
    setProfile(p)
  }, [])

  // ── N.I.N.A. 감시: 장비 연결 이후 화면에서 N.I.N.A.가 꺼지거나 멈추면 알린다 ─────────
  const [nina, setNina] = useState<NinaState>('Unknown')
  const watching = WATCHED.includes(phase)
  useEffect(() => {
    if (!watching) return
    const source = new EventSource('/api/nina/watch')
    // 서버는 상태 이름을 글자 그대로 보낸다 (Running · Exited · NotResponding)
    source.addEventListener('nina', (e) => setNina(String((e as MessageEvent).data).replace(/"/g, '') as NinaState))
    return () => source.close()
  }, [watching])
  const ninaLost = watching && (nina === 'Exited' || nina === 'NotResponding')
  useEffect(() => {
    // 끊기면 상태 줄의 장비 점도 모두 꺼진다
    if (ninaLost) setDevices((d) => d?.map((x) => ({ ...x, connected: false })) ?? d)
  }, [ninaLost])
  const restartNina = useCallback(async () => {
    if (phase === 'site' || phase === 'preflight' || phase === 'rig' || phase === 'plan' || phase === 'target' || phase === 'wrap') resumeTo.current = phase
    // 촬영 중이었으면: 촬영을 멈추고(가이딩 정지 확인) 다시 켠 뒤 대상 단계를 이동부터 (적도의 위치·가이딩을 알 수 없으므로 — CX-APP-R4)
    if (phase === 'shoot') {
      resumeTo.current = 'target'
      await fetch('/api/shoot/abort-for-restart', { method: 'POST' }).catch(() => null)
    }
    setNina('Unknown')
    fadeTo('engine')
  }, [phase, fadeTo])
  const toRig = useCallback(() => fadeTo('rig'), [fadeTo])
  const toPlan = useCallback(() => fadeTo('plan'), [fadeTo])
  const toTarget = useCallback(() => fadeTo('target'), [fadeTo])
  const toShoot = useCallback(() => fadeTo('shoot'), [fadeTo])
  const toWrap = useCallback(() => fadeTo('wrap'), [fadeTo])
  const toSummary = useCallback(() => fadeTo('summary'), [fadeTo])

  // 촬영에 들어가면 적색 테마로 (DESIGN.md 7-3, 2026-10-07). 다른 대상으로 돌아가면(계획) 원래 테마로, 마무리·요약은 그대로
  const themeBefore = useRef<typeof theme | null>(null)
  useEffect(() => {
    if (phase === 'shoot' && theme !== 'night') {
      themeBefore.current = theme
      setTheme('night')
    } else if (phase === 'plan' && themeBefore.current) {
      setTheme(themeBefore.current)
      themeBefore.current = null
    }
    // 테마를 직접 바꾸면 그 선택을 따른다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])
  // 장비 준비가 끝나면 대상으로: 대상이 장비 준비 작업을 다시 하자고 해 멈춰 둔 것이면 그 대상을 이어서, 아니면 계획부터
  const rigDone = useCallback(async () => {
    const target = await prepareState('target')
    if (target?.handoff === 'rig') return fadeTo('target')
    // 계획으로: 그동안 적도의는 홈에서 추적을 끄고 기다린다 (2026-10-08 사용자 결정)
    void fetch('/api/prepare/rig/rest', { method: 'POST' }).catch(() => null)
    fadeTo('plan')
  }, [fadeTo])
  const handoff = useCallback((to: PrepGroup) => fadeTo(to === 'rig' ? 'rig' : 'target'), [fadeTo])
  const frame = useFrame()
  const siteInfo = describeSite(site ?? null, profile, profiles ?? [])

  return (
    <div className={styles.window}>
    <div
      className={styles.shell}
      data-fixed={frame.fixed}
      data-frame=""
      style={{ '--frame-scale': frame.scale } as CSSProperties}
    >
      {phase !== 'boot' && (
        <div className={styles.bar}>
          <StatusBar
            profile={profile}
            profiles={profiles ?? []}
            onSwitchProfile={switchProfile}
            onProfileChange={updateProfile}
            devices={devices}
            label={LABEL_OF[phase] ?? '소프트웨어 준비'}
            site={
              SITE_SHOWN.includes(phase) && site !== undefined
                ? {
                    current: site,
                    name: siteInfo.saved?.name ?? null,
                    otherName: siteInfo.otherName,
                    syncing: siteSyncing,
                    editable: SITE_EDITABLE.includes(phase),
                  }
                : null
            }
            onPickSite={pickSite}
            onEditSites={editSites}
            theme={theme}
            onThemeChange={setTheme}
          />
        </div>
      )}
      {/* 전환 중(settled=false)에는 화면 전체를 누를 수 없고 초점도 받지 않는다 (inert) */}
      {/* 화면 영역(왼쪽) + 진행 표시(오른쪽 세로 열). 화면 영역이 크기 기준 틀이라 화면 안의 cqw·cqh는 이 영역 기준 */}
      <div className={styles.body} data-sky={!!railExtra?.sky}>
      <ScreenReady.Provider value={settled}>
      <RailProvider onChange={setRailExtra}>
      <div className={styles.screen} data-shown={shown} inert={!settled}>
        {phase === 'boot' && <BootScreen error={loadError ? `${PRODUCT.name} 내부 서버에 연결하지 못했습니다. ${PRODUCT.reul} 다시 실행해 주세요.` : null} />}
        {phase === 'profiles' && (
          <ProfileScreen
            profiles={profiles ?? []}
            onSelect={choose}
            onNew={() => {
              setEditing(null)
              setPhase('newProfile')
            }}
            onEdit={(p) => {
              setEditing(p)
              setPhase('newProfile')
            }}
          />
        )}
        {phase === 'newProfile' && (
          <NewProfileScreen
            editing={editing}
            onCreated={created}
            onEdited={(p) => {
              updateProfile(p)
              setEditing(null)
              setPhase('profiles')
            }}
            onCancel={(profiles ?? []).length > 0 ? () => setPhase('profiles') : undefined}
          />
        )}
        {phase === 'check' && <SetupCheckScreen onContinue={toEngine} />}
        {phase === 'engine' && <EngineStartScreen onContinue={toEquipment} />}
        {phase === 'equipment' && (
          <EquipmentScreen onContinue={toNext} site={site === undefined ? undefined : { current: site, name: siteInfo.saved?.name ?? null }} />
        )}
        {phase === 'site' && profile && (
          <SiteScreen
            profile={profile}
            current={site ?? null}
            onProfileChange={updateProfile}
            onApplied={setSite}
            onDone={() => fadeTo(siteReturn.current)}
          />
        )}
        {phase === 'preflight' && <PreflightScreen onContinue={toRig} />}
        {phase === 'rig' && <PrepareScreen key="rig" group="rig" onDone={() => void rigDone()} />}
        {phase === 'plan' && <PlanScreen onContinue={toTarget} />}
        {phase === 'target' && <PrepareScreen key="target" group="target" onDone={toShoot} onReplan={toPlan} onHandoff={handoff} />}
        {phase === 'shoot' && <ShootScreen onWrap={toWrap} onRetarget={toPlan} />}
        {phase === 'wrap' && <PrepareScreen key="wrap" group="wrap" onDone={toSummary} />}
        {phase === 'summary' && <SummaryScreen />}
      </div>
      </RailProvider>
      </ScreenReady.Provider>
      {/* 진행 표시는 화면 전환 페이드 밖에 둔다: 화면이 바뀌어도 같은 자리에 그대로 */}
      {STAGE_OF[phase] && (
        <div className={styles.rail}>
          <StepRail current={STAGE_OF[phase]} extra={railExtra} />
        </div>
      )}
      </div>
      {ninaLost && <NinaLostCard state={nina} onRestart={() => void restartNina()} />}
      <ConfirmDialog
        open={pending !== null}
        message={
          pending?.busy
            ? '장비를 멈추고, 전에 끝낸 작업이 그대로인지 확인하는 중이에요…'
            : pending?.error
              ? `${pending.error} (${pending.summary})`
              : pending?.kind === 'resume'
                ? `오늘 밤 진행 기록이 있어요 · ${pending.summary}. 이어서 할까요? 출발 전 점검은 건너뛰고, 끝낸 작업은 장비가 그대로인지 확인한 뒤 건너뛰어요.`
                : (pending?.summary ?? '')
        }
        confirmLabel={pending?.kind === 'resume' ? (pending.error ? '다시 시도' : '이어서 하기') : '확인'}
        cancelLabel="새로 시작"
        single={pending?.kind !== 'resume'}
        dismissable={false}
        onConfirm={() => void answerPending(true)}
        onCancel={() => void answerPending(false)}
      />
    </div>
    </div>
  )
}
