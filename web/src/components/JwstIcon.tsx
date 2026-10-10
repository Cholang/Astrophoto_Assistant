import { useId, type SVGProps } from 'react'

/** 거울 한 장 (육각형) */
const SEG = 'M0 -42 36.37 -21 36.37 21 0 42 -36.37 21 -36.37 -21Z'
/** 거울 18장의 자리 (가운데 구멍 없이) */
const MIRRORS: [number, number][] = [
  [-145.49, 0], [-109.12, 63], [-72.75, 126], [-109.12, -63], [-72.75, 0], [-36.37, 63], [0, 126], [-72.75, -126], [-36.37, -63],
  [36.37, 63], [72.75, 126], [0, -126], [36.37, -63], [72.75, 0], [109.12, 63], [72.75, -126], [109.12, -63], [145.49, 0],
]

/**
 * 망원경 아이콘: 제임스 웹 우주 망원경 (mockups/aira-brand/jwst-icon-line.svg — 2026-10-11 사용자 제공 그림을 아이라 선 아이콘으로).
 * 색은 글자색(currentColor) 선 + 아주 옅은 면. 선은 70px 안팎에서 Lucide 아이콘처럼 가늘게 보이도록 맞춤.
 * 면이 있어(옅게) 아이콘 모양 자체를 누를 수 있다 (장비 연결 화면의 망원경).
 */
export default function JwstIcon(props: SVGProps<SVGSVGElement>) {
  const seg = useId()
  return (
    <svg viewBox="0 0 512 512" fill="none" stroke="currentColor" strokeLinejoin="round" strokeLinecap="round" aria-hidden="true" {...props}>
      <defs>
        <path id={seg} d={SEG} />
      </defs>
      {/* 차양막: 세 겹을 선 하나씩으로 */}
      <path d="M74 384 247 347 438 387 269 445Z" strokeWidth={6} fill="currentColor" fillOpacity={0.05} />
      <path d="M74 404 269 465 438 407" strokeWidth={5} opacity={0.55} />
      <path d="M74 424 269 485 438 427" strokeWidth={4.5} opacity={0.3} />
      <path d="M240 338 262 337 272 372" strokeWidth={6} />
      {/* 주경: 육각 거울 18장 + 보조경 지지대 셋 */}
      <g transform="translate(256 215) rotate(-8)">
        <g strokeWidth={6.5} fill="currentColor" fillOpacity={0.08}>
          {MIRRORS.map(([x, y]) => (
            <use key={`${x},${y}`} href={`#${seg}`} transform={`translate(${x} ${y})`} />
          ))}
        </g>
        <path d="M0 -166 112 56 -144 84 M112 56 38 145" strokeWidth={6.5} />
        <circle cx={112} cy={56} r={18} strokeWidth={6.5} fill="currentColor" fillOpacity={0.15} />
      </g>
    </svg>
  )
}
