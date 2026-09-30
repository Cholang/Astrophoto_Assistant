import { useCallback, useEffect, useRef, useState } from 'react'
import StatusBar, { type DeviceState } from './components/StatusBar'
import NinaLostCard, { type NinaState } from './components/NinaLostCard'
import StepRail, { type Stage } from './components/StepRail'
import { listProfiles, selectProfile, type Profile } from './profiles'
import BootScreen from './screens/BootScreen'
import EngineStartScreen from './screens/EngineStartScreen'
import EquipmentScreen from './screens/EquipmentScreen'
import NewProfileScreen from './screens/NewProfileScreen'
import PlanScreen from './screens/PlanScreen'
import PreflightScreen from './screens/PreflightScreen'
import ProfileScreen from './screens/ProfileScreen'
import SetupCheckScreen from './screens/SetupCheckScreen'
import { PRODUCT } from './product'
import { useTheme } from './theme'
import styles from './App.module.css'

// flow.mmd ① 시작 · 연결:
// 부팅 로고 → 프로필 선택 (없으면 새 프로필 만들기) → 0단계 설치 확인 → 1단계 엔진 켜기 → 장비 연결
// → 출발 전 점검 → 촬영 계획 → (다음: 준비)
type Phase = 'boot' | 'profiles' | 'newProfile' | 'check' | 'engine' | 'equipment' | 'preflight' | 'plan' | 'prepare'

/** 화면 → 하단 단계 레일의 단계. 부팅·프로필 화면에는 레일이 없다 (DESIGN.md 2장) */
const STAGE_OF: Partial<Record<Phase, Stage>> = {
  check: '연결',
  engine: '연결',
  equipment: '연결',
  preflight: '점검',
  plan: '계획',
  prepare: '준비',
}

/** N.I.N.A.가 켜져 있어야 하는 화면 (1단계 엔진 켜기 이후). 여기서 N.I.N.A.가 꺼지면 알린다 */
const WATCHED: Phase[] = ['equipment', 'preflight', 'plan', 'prepare']

/** 상단 상태 줄의 지금 단계 이름 */
const LABEL_OF: Partial<Record<Phase, string>> = {
  equipment: '장비 연결',
  preflight: '출발 전 점검',
  plan: '촬영 계획',
  prepare: '준비',
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
  const fading = useRef(false)

  /** 지금 화면을 서서히 감추고, 다음 화면을 서서히 보여 준다. */
  const fadeTo = useCallback((next: Phase) => {
    if (fading.current) return
    fading.current = true
    setShown(false)
    setTimeout(() => {
      setPhase(next)
      // 다음 화면이 투명한 상태로 한 번 그려진 뒤에 보이게 해야 페이드인이 된다
      requestAnimationFrame(() =>
        requestAnimationFrame(() => {
          setShown(true)
          fading.current = false
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
  const toNext = useCallback((items: { title: string; term: string | null; status: string }[]) => {
    // 상태 줄에는 장비 이름(OnStep 등)을 쓴다. 칸의 큰 글씨는 장비 종류, 작은 글씨(term)가 장비 이름
    setDevices(items.filter((i) => i.status !== 'Absent').map((i) => ({ name: i.term ?? i.title, connected: i.status === 'Pass' })))
    const back = resumeTo.current
    resumeTo.current = null
    fadeTo(back ?? 'preflight')
  }, [fadeTo])

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
  const restartNina = useCallback(() => {
    if (phase === 'preflight' || phase === 'plan' || phase === 'prepare') resumeTo.current = phase
    setNina('Unknown')
    fadeTo('engine')
  }, [phase, fadeTo])
  const toPlan = useCallback(() => fadeTo('plan'), [fadeTo])
  const toPrepare = useCallback(() => fadeTo('prepare'), [fadeTo])

  return (
    <div className={styles.shell}>
      {phase !== 'boot' && (
        <div className={styles.bar}>
          <StatusBar
            profile={profile}
            devices={devices}
            label={LABEL_OF[phase] ?? '소프트웨어 준비'}
            theme={theme}
            onThemeChange={setTheme}
          />
        </div>
      )}
      <div className={styles.screen} data-shown={shown}>
        {phase === 'boot' && <BootScreen error={loadError ? `${PRODUCT.name} 내부 서버에 연결하지 못했습니다. ${PRODUCT.reul} 다시 실행해 주세요.` : null} />}
        {phase === 'profiles' && <ProfileScreen profiles={profiles ?? []} onSelect={choose} onNew={() => setPhase('newProfile')} />}
        {phase === 'newProfile' && (
          <NewProfileScreen onCreated={created} onCancel={(profiles ?? []).length > 0 ? () => setPhase('profiles') : undefined} />
        )}
        {phase === 'check' && <SetupCheckScreen onContinue={toEngine} />}
        {phase === 'engine' && <EngineStartScreen onContinue={toEquipment} />}
        {phase === 'equipment' && <EquipmentScreen onContinue={toNext} />}
        {phase === 'preflight' && <PreflightScreen onContinue={toPlan} />}
        {phase === 'plan' && <PlanScreen onContinue={toPrepare} />}
        {phase === 'prepare' && (
          <main className={styles.placeholder}>
            <h1>촬영 준비</h1>
            <p>초점·극축 정렬·대상 찾기 화면은 다음에 만들 차례입니다.</p>
          </main>
        )}
      </div>
      {ninaLost && <NinaLostCard state={nina} onRestart={restartNina} />}
      {/* 레일은 화면 전환 페이드 밖에 둔다: 화면이 바뀌어도 같은 자리에 그대로 */}
      {STAGE_OF[phase] && (
        <div className={styles.rail}>
          <StepRail current={STAGE_OF[phase]} />
        </div>
      )}
    </div>
  )
}
