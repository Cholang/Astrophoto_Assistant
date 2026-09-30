import { Camera, Crosshair, Disc3, Focus, PlugZap, Rotate3d, Sun, Telescope, type LucideProps } from 'lucide-react'

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
} as const

export default function DeviceIcon({ kind, ...props }: { kind: string } & LucideProps) {
  const Icon = ICONS[kind as keyof typeof ICONS] ?? Camera
  return <Icon strokeWidth={1.8} aria-hidden="true" {...props} />
}
