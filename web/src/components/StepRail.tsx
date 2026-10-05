import { Check, LogOut } from 'lucide-react'
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { closeApp, inDesktop } from '../host'
import styles from './StepRail.module.css'

/** 촬영 단계 (DESIGN.md "진행 표시"). 연결 = 설치 확인·엔진 켜기·장비 연결 */
export const STAGES = ['연결', '점검', '계획', '준비', '촬영', '마무리'] as const
export type Stage = (typeof STAGES)[number]

/** 지금 단계 아래에 펼치는 작업 한 줄 (예: 준비의 극축 정렬 … 시험 사진) */
export interface RailItem {
  id: string
  label: string
  state: 'done' | 'now' | 'todo' | 'problem' | 'skipped'
}

/** 화면이 진행 표시에 넘겨주는 것: 지금 단계의 작업 목록과 맨 아래 버튼(예: 준비 중단) */
export interface RailExtra {
  items?: RailItem[]
  action?: { label: string; onClick: () => void }
  /** 화면이 하늘 화면(준비 단계)이면 진행 표시도 하늘 위에 얹는다 */
  sky?: boolean
}

const RailContext = createContext<(extra: RailExtra | null) => void>(() => {})

/** 진행 표시 통로: App이 감싸고, 화면은 useRailExtra로 작업 목록을 넘긴다 */
export function RailProvider({ children, onChange }: { children: ReactNode; onChange: (extra: RailExtra | null) => void }) {
  return <RailContext.Provider value={onChange}>{children}</RailContext.Provider>
}

/** 화면에서: 지금 단계의 작업 목록·버튼을 진행 표시에 보낸다 (화면을 떠나면 지움) */
export function useRailExtra(extra: RailExtra | null) {
  const set = useContext(RailContext)
  const key = JSON.stringify(extra?.items ?? null) + (extra?.action?.label ?? '') + (extra?.sky ? 'sky' : '')
  useEffect(() => {
    set(extra)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, set])
  useEffect(() => () => set(null), [set])
}

/**
 * 진행 표시: 화면 오른쪽 세로 열 (2026-10-05 사용자 결정, 시안 v8). 단계 → (지금 단계의) 작업.
 * 작업은 제목만(결과·세부 과정 없음). 지금 작업만 보이고, 나머지는 마우스를 올리거나 키보드 초점일 때 보인다. 맨 아래 화면 버튼(준비 중단)·앱 끄기.
 * 화면이 바뀌어도 같은 자리·같은 너비.
 */
export default function StepRail({ current, extra }: { current: Stage; extra?: RailExtra | null }) {
  const now = STAGES.indexOf(current)
  const [open, setOpen] = useState<string | null>(null)
  return (
    <nav className={styles.rail} aria-label="촬영 진행">
      <ol className={styles.list}>
        {STAGES.map((s, i) => {
          const state = i < now ? 'done' : i === now ? 'now' : 'todo'
          const items = i === now ? extra?.items : undefined
          return (
            <li key={s} className={styles.stage} data-state={state} data-last={i === STAGES.length - 1}>
              <span className={styles.row} aria-current={state === 'now' ? 'step' : undefined}>
                <span className={styles.name}>{s}</span>
                <span className={styles.dot}>{state === 'done' && <Check strokeWidth={2.5} />}</span>
              </span>
              {items && items.length > 0 && (
                <ol className={styles.items} aria-label={`${s} 작업`}>
                  {items.map((it) => (
                    <li
                      key={it.id}
                      className={styles.item}
                      data-state={it.state}
                      data-open={open === it.id}
                      tabIndex={0}
                      onMouseEnter={() => setOpen(it.id)}
                      onMouseLeave={() => setOpen(null)}
                      onFocus={() => setOpen(it.id)}
                      onBlur={() => setOpen(null)}
                      onClick={() => setOpen(open === it.id ? null : it.id)}
                      aria-current={it.state === 'now' ? 'step' : undefined}
                      aria-label={it.label}
                    >
                      <span className={styles.itemName}>{it.label}</span>
                      <span className={styles.itemDot} />
                    </li>
                  ))}
                </ol>
              )}
            </li>
          )
        })}
      </ol>
      <div className={styles.end}>
        {extra?.action && (
          <button type="button" className={styles.action} onClick={extra.action.onClick}>
            {extra.action.label}
          </button>
        )}
        {/* 앱 끄기: 전체화면이라 창 닫기 버튼이 없어서 둔다 (데스크톱 창에서만) */}
        {inDesktop() && (
          <button type="button" className={styles.exit} onClick={closeApp} aria-label="앱 끄기" title="앱 끄기">
            <LogOut strokeWidth={2} aria-hidden="true" />
            <span>앱 끄기</span>
          </button>
        )}
      </div>
    </nav>
  )
}
