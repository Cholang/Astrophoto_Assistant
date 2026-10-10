import { useEffect, useState } from 'react'
import LiveView from '../components/LiveView'
import PrepCenter from '../components/PrepCenter'
import PrepGuide from '../components/PrepGuide'
import { useScreenSky } from '../components/ScreenSky'
import { useRailExtra } from '../components/StepRail'
import { closeApp } from '../host'
import { PRODUCT } from '../product'
import { hm, nightSummary, type NightSummary } from '../shoot'
import styles from './PrepareScreen.module.css'

/**
 * 오늘 밤 요약 (마무리 끝, 시안 v10): 대상별 쓸 사진·누적·제외 폴더, 보정 프레임, 폴더·등급 분포.
 * 진행 표시는 모든 단계 완료(작업 묶음 접음), 아래 종료 버튼은 숨기고 가운데 "AA 종료"만 (같은 버튼이 두 곳에 보이지 않게).
 */
export default function SummaryScreen() {
  const [s, setS] = useState<NightSummary | null>(null)
  const [note, setNote] = useState<string | null>(null)

  useEffect(() => {
    void nightSummary().then(setS)
  }, [])

  useScreenSky(true)
  // 레일 아래 종료 버튼은 App이 이 화면에서 숨긴다(가운데에 종료 버튼이 있어서)
  useRailExtra({ stage: '마무리', complete: true })

  const openFolder = async () => {
    const r = await fetch('/api/night/open-folder', { method: 'POST' }).catch(() => null)
    if (!r?.ok) setNote('오늘 밤 사진 폴더를 찾지 못했습니다.')
  }

  const rows = s?.targets ?? []
  const cal = s?.flat?.skipped
    ? null
    : s?.dark
      ? `${s.flat?.count ?? 0} · ${s.dark.flatDarks} · ${s.dark.sets.reduce((n, d) => n + d.count, 0)}장`
      : null
  const grades = s ? Object.entries(s.tally).map(([g, n]) => `${g} ${n}`).join(' · ') : ''
  // 원인별로 멈춘 시간 (구름 24분 · 빛 2분 …)
  const LABEL: Record<string, string> = { cloud: '구름', light: '빛', jump: '바람·케이블', guider: '장비 끊김', mount: '적도의', hotpixel: '가이드 별 다시 고르기', unstable: '가이딩 불안정', frames: '별 없는 사진' }
  const pausedAll: Record<string, number> = {}
  for (const t of rows) for (const [k, m] of Object.entries(t.pausedMinutes ?? {})) pausedAll[k] = (pausedAll[k] ?? 0) + m
  const paused = Object.entries(pausedAll).filter(([, m]) => m >= 1).map(([k, m]) => `${LABEL[k] ?? k} ${Math.round(m)}분`).join(' · ')
  const pack = s?.pack
  // 제외할 사진인데 제외 폴더로 옮기지 못한 것 (CX-APP-R2) — 직접 옮기도록 알린다
  const notMoved = rows.reduce((n, t) => n + (t.notMoved?.length ?? 0), 0)
  const status = note
    ? { text: note, tone: 'Warn' as const }
    : notMoved
      ? { text: `제외 폴더로 옮기지 못한 사진이 ${notMoved}장 있어요 · 폴더에서 직접 "제외" 폴더로 옮겨 주세요`, tone: 'Warn' as const }
    : pack
      ? {
          text: `${pack.homed ? '적도의는 홈' : pack.userConfirmed ? '적도의는 직접 확인함' : '적도의 홈 확인 못 함'}${pack.trackingOff || pack.userConfirmed ? '' : ' · 추적 끄기 확인 못 함'}, ${pack.disconnected ? '장비 연결은 끊었어요' : '장비 연결이 일부 남아 있어요'}`,
          tone: pack.safeToPowerOff ? ('Ok' as const) : ('Warn' as const),
        }
      : null

  return (
    <main className={styles.stage}>
      <LiveView current={null} />
      <div className={styles.scrim} />
      <div className={styles.over}>
        <PrepGuide
          task="마무리"
          title="오늘 밤 촬영을 마쳤어요"
          text={`찍은 사진과 보정 프레임은 오늘 밤 폴더에 대상별로 정리했어요. 제외 폴더의 사진은 스태킹에 넣지 않으면 돼요. ${
            pack?.safeToPowerOff ? '장비 전원을 끄셔도 돼요.' : '전원을 끄기 전에 적도의가 멈춰 있고 장비 연결이 끊겼는지 확인해 주세요.'
          }${
            // 가이드 카메라가 핫픽셀을 별로 잡은 일이 있었으면 한 줄 팁 (2026-10-08 사용자 결정)
            pausedAll.hotpixel !== undefined
              ? ' 오늘 가이드 카메라가 핫픽셀을 별로 착각한 일이 있었어요. 낮이나 집에서 PHD2의 다크 라이브러리를 한 번 만들어 두면 줄일 수 있어요.'
              : ''
          }`}
        />
        <PrepCenter
          readout={null}
          status={status}
          actions={[
            { id: 'quit', label: `${PRODUCT.name} 종료`, primary: true },
            { id: 'folder', label: '폴더 열기', primary: false },
          ]}
          onAct={(id) => (id === 'quit' ? closeApp() : void openFolder())}
          custom={
            s && (
              <div className={styles.metricPlain}>
                <div className={styles.nightTable}>
                  <span className={styles.hd}>대상</span>
                  <span className={styles.hd}>쓸 사진</span>
                  <span className={styles.hd}>누적</span>
                  <span className={styles.hd}>제외 폴더</span>
                  {rows.map((t, i) => [
                    <b key={`n${i}`}>{t.name}</b>,
                    <b key={`g${i}`}>{t.good}장</b>,
                    <b key={`t${i}`}>{hm(t.good * t.exposureSeconds)}</b>,
                    <span key={`x${i}`}>{t.excluded}장</span>,
                  ])}
                  <span>플랫 · 플랫 다크 · 다크</span>
                  <b>{cal ?? '건너뜀'}</b>
                  <span>{s.flat && !s.flat.skipped ? `노출 ${s.flat.exposureSeconds}초` : ''}</span>
                  <span />
                </div>
                <span>{[s.folder, grades && `등급 ${grades}`, paused && `멈춘 시간 ${paused}`].filter(Boolean).join(' · ')}</span>
              </div>
            )
          }
        />
      </div>
    </main>
  )
}
