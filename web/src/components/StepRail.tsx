import { Check, LogOut } from 'lucide-react'
import { Fragment } from 'react'
import { closeApp, inDesktop } from '../host'
import styles from './StepRail.module.css'

/** 촬영 단계 (DESIGN.md 2장 "하단 단계 레일"). 연결 = 설치 확인·엔진 켜기·장비 연결 */
export const STAGES = ['연결', '점검', '계획', '준비', '촬영', '마무리'] as const
export type Stage = (typeof STAGES)[number]

/**
 * 화면 맨 아래 가로 한 줄 단계 표시. 화면이 바뀌어도 같은 위치·높이에 있다.
 * 처음부터 현재 단계까지의 선은 강조색 (온도계 게이지처럼 차오름).
 */
export default function StepRail({ current }: { current: Stage }) {
  const now = STAGES.indexOf(current)
  return (
    <nav className={styles.rail} aria-label="촬영 단계">
      {STAGES.map((s, i) => {
        const state = i < now ? 'done' : i === now ? 'now' : 'todo'
        return (
          <Fragment key={s}>
            {i > 0 && <span className={styles.sep} data-on={i <= now} aria-hidden="true" />}
            <span className={styles.step} data-state={state} aria-current={state === 'now' ? 'step' : undefined}>
              <span className={styles.mark}>{state === 'done' && <Check strokeWidth={2.5} />}</span>
              {s}
            </span>
          </Fragment>
        )
      })}
      {/* 앱 끄기: 레일 맨 오른쪽. 전체화면이라 창 닫기 버튼이 없어서 둔다 (데스크톱 창에서만) */}
      {inDesktop() && (
        <button type="button" className={styles.exit} onClick={closeApp} aria-label="앱 끄기" title="앱 끄기">
          <LogOut strokeWidth={2} aria-hidden="true" />
        </button>
      )}
    </nav>
  )
}
