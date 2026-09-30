import { Aperture, Camera, Crosshair, Disc3, Focus, PlugZap, Rotate3d, Sun, Telescope, type LucideProps } from 'lucide-react'

// 장비 종류(서버 EquipmentConnector의 Kind) → 아이콘
const ICONS = {
  switch: PlugZap,
  mount: Telescope,
  camera: Camera,
  focuser: Focus,
  filterwheel: Disc3,
  rotator: Rotate3d,
  flatdevice: Sun,
  guider: Crosshair,
  // 경통(광학): 연결 장비가 아니라 AA의 경통 목록 — 조리개 모양
  scope: Aperture,
} as const

export default function DeviceIcon({ kind, ...props }: { kind: string } & LucideProps) {
  const Icon = ICONS[kind as keyof typeof ICONS] ?? Camera
  return <Icon strokeWidth={1.8} aria-hidden="true" {...props} />
}
