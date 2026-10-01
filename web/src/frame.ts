import { useEffect, useState } from 'react'

/**
 * 기준 화면 (DESIGN.md 1장 "기준 화면"): 1920×1200 (16:10).
 * 현장 노트북 3840×2400을 배율 200%로 쓸 때, 그리고 회사 모니터 1920×1200(100%)에서 앱이 느끼는 크기 — 둘 다 꼭 맞는다.
 * 데스크톱 크기 창에서는 앱을 항상 이 크기로 그린 뒤, 창에 맞춰 통째로 확대·축소한다.
 * 그래서 다른 비율에서도 깨지지 않고, 16:10보다 넓으면(16:9·울트라와이드) 좌우, 좁으면 위아래가 같은 배경의 여백이 된다.
 * 화면 안의 크기는 창(vw·vh)이 아니라 이 틀(cqw·cqh, App.module.css의 .shell이 크기 컨테이너)을 기준으로 쓴다.
 */
export const FRAME_W = 1920
export const FRAME_H = 1200

/**
 * 이보다 작게 줄여야 하는 창(폰, 아주 작은 창)에서는 확대·축소하지 않고 창 크기 그대로 그린다
 * (글자가 너무 작아지지 않게. 좁은 화면용 배치는 각 화면의 @media 규칙이 맡는다).
 * 0.6 = 기준 화면이 1152×720보다 작게 줄어드는 경우
 */
const MIN_SCALE = 0.6

export interface Frame {
  /** true면 기준 틀(FRAME_W×FRAME_H)을 scale로 확대·축소해 가운데에 그린다 */
  fixed: boolean
  scale: number
}

function measure(): Frame {
  const scale = Math.min(window.innerWidth / FRAME_W, window.innerHeight / FRAME_H)
  return scale >= MIN_SCALE ? { fixed: true, scale } : { fixed: false, scale: 1 }
}

/** 창 크기가 바뀔 때마다 틀의 배율을 다시 잰다 */
export function useFrame(): Frame {
  const [frame, setFrame] = useState(measure)
  useEffect(() => {
    const update = () => setFrame((f) => {
      const next = measure()
      return next.fixed === f.fixed && next.scale === f.scale ? f : next
    })
    window.addEventListener('resize', update)
    return () => window.removeEventListener('resize', update)
  }, [])
  return frame
}

/**
 * 화면에서 잰 길이(getBoundingClientRect, 확대·축소가 적용된 값)를 틀 안의 길이로 바꾸는 배율.
 * el은 틀 안의 아무 요소. 확대·축소가 없으면 1
 */
export function scaleOf(el: HTMLElement): number {
  const w = el.offsetWidth
  return w > 0 ? el.getBoundingClientRect().width / w : 1
}
