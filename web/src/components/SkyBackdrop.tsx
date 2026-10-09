import { useEffect, useRef } from 'react'
import styles from './SkyBackdrop.module.css'

/**
 * 앱 전체 배경의 은은한 효과 (2026-10-09 사용자 선택 — Codex 시안 mockups/aira-background.html "성운과 별", 세기 1.1):
 * 성운 두 층이 서로 다른 방향으로 천천히 흐르고, 별은 잠깐씩 나타나 빛났다 사라지며(동시에 6개까지), 혜성 하나가 약 30분에 걸쳐 지나간다.
 * 성운 무늬는 테마가 바뀔 때만 한 번 만든다(프레임마다 아님). 초당 30번까지만 그리고, 창이 가려지면 멈춘다(노트북 배터리).
 * 동작 줄이기면 멈춘 그림 한 장(혜성·반짝임 없음). 촬영 테마는 붉은빛으로 더 약하게
 */
const LEVEL = 1.1

type Theme = 'dark' | 'light' | 'night'

/** 별·혜성 색 (r, g, b) — 시안의 --star */
const STAR: Record<Theme, string> = { dark: '235, 232, 227', light: '60, 62, 66', night: '150, 0, 0' }
/** 성운 두 층의 색 */
const NEBULA: Record<Theme, [number, number, number][]> = {
  dark: [[92, 132, 174], [147, 105, 157]],
  light: [[70, 103, 156], [139, 86, 143]],
  night: [[110, 0, 0], [80, 0, 0]],
}

const themeOf = (): Theme => {
  const t = document.documentElement.dataset.theme
  return t === 'light' || t === 'night' ? t : 'dark'
}

const rand = (a: number, b: number) => a + Math.random() * (b - a)
const hash = (x: number, y: number) => {
  const n = Math.sin(x * 127.1 + y * 311.7) * 43758.5453
  return n - Math.floor(n)
}
function noise(x: number, y: number) {
  const ix = Math.floor(x), iy = Math.floor(y)
  let u = x - ix, v = y - iy
  u = u * u * (3 - 2 * u)
  v = v * v * (3 - 2 * v)
  return (hash(ix, iy) * (1 - u) + hash(ix + 1, iy) * u) * (1 - v) + (hash(ix, iy + 1) * (1 - u) + hash(ix + 1, iy + 1) * u) * v
}
function fbm(x: number, y: number) {
  let n = 0, a = 0.5
  for (let i = 0; i < 5; i++) {
    n += a * noise(x, y)
    x = x * 2.03 + 13
    y = y * 2.03 + 7
    a *= 0.5
  }
  return n
}

/** 성운 한 층의 무늬 (640×400, 늘려 그린다) */
function nebula(index: number, theme: Theme) {
  const c = document.createElement('canvas')
  c.width = 640
  c.height = 400
  const g = c.getContext('2d')!
  const d = g.createImageData(640, 400)
  const [r, gr, b] = NEBULA[theme][index]
  const gain = theme === 'light' ? 560 : 240
  for (let y = 0; y < 400; y++)
    for (let x = 0; x < 640; x++) {
      const u = x / 640, v = y / 400
      const wx = u * 4 + index * 17, wy = v * 3 + index * 9
      const warp = fbm(wx, wy), n = fbm(wx + warp * 2, wy + warp * 1.4)
      const band = Math.exp(-Math.pow((v - (0.67 - 0.4 * u + 0.12 * Math.sin(u * 5 + index * 2))) / 0.25, 2))
      const edge = Math.pow(Math.sin(Math.PI * u) * Math.sin(Math.PI * v), 0.65)
      const i = (y * 640 + x) * 4
      d.data[i] = r
      d.data[i + 1] = gr
      d.data[i + 2] = b
      d.data[i + 3] = Math.min(255, Math.pow(Math.max(0, n - 0.24), 1.65) * band * edge * gain)
    }
  g.putImageData(d, 0, 0)
  return c
}

interface Comet { start: number; duration: number; x0: number; y0: number; x1: number; y1: number; x2: number; y2: number }
/** 혜성 하나: 왼쪽 아래 → 오른쪽 위, 28~32분 (좌표는 창 비율이라 크기가 바뀌어도 길이 같음) */
const newComet = (start: number): Comet => ({
  start, duration: rand(28, 32) * 60,
  x0: rand(0.04, 0.12), y0: rand(0.82, 0.94), x1: rand(0.4, 0.55), y1: rand(0.28, 0.48), x2: rand(1.12, 1.22), y2: rand(-0.24, -0.16),
})

export default function SkyBackdrop() {
  const ref = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const cv = ref.current
    if (!cv) return
    const ctx = cv.getContext('2d')
    if (!ctx) return
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)')
    let W = 0, H = 0, time = 0, nextStar = 2, previous = 0, raf = 0
    let theme = themeOf()
    let color = STAR[theme]
    let clouds: HTMLCanvasElement[] = []
    let stars: { x: number; y: number; r: number; a: number }[] = []
    let glints: { x: number; y: number; start: number; duration: number; r: number }[] = []
    let comet = newComet(0)

    const drawComet = (k: number) => {
      if (time - comet.start >= comet.duration) comet = newComet(time)
      const c = comet, u = Math.min(1, (time - c.start) / c.duration), v = 1 - u
      const x = (v * v * c.x0 + 2 * v * u * c.x1 + u * u * c.x2) * W
      const y = (v * v * c.y0 + 2 * v * u * c.y1 + u * u * c.y2) * H
      const dx = (v * (c.x1 - c.x0) + u * (c.x2 - c.x1)) * W
      const dy = (v * (c.y1 - c.y0) + u * (c.y2 - c.y1)) * H
      const length = Math.min(72, W * 0.055, H * 0.09)
      const alpha = Math.min(1, k) * Math.min(1, (time - c.start) / 5)
      ctx.save()
      ctx.translate(x, y)
      ctx.rotate(Math.atan2(dy, dx))
      const tail = ctx.createLinearGradient(-length, 0, 0, 0)
      tail.addColorStop(0, `rgba(${color},0)`)
      tail.addColorStop(0.65, `rgba(${color},${alpha * 0.035})`)
      tail.addColorStop(1, `rgba(${color},${alpha * 0.24})`)
      ctx.fillStyle = tail
      ctx.beginPath()
      ctx.moveTo(0, 0)
      ctx.quadraticCurveTo(-length * 0.4, -3, -length, -5)
      ctx.quadraticCurveTo(-length * 0.65, 2, 0, 1)
      ctx.fill()
      const halo = ctx.createRadialGradient(0, 0, 0, 0, 0, 6)
      halo.addColorStop(0, `rgba(${color},${alpha * 0.3})`)
      halo.addColorStop(1, `rgba(${color},0)`)
      ctx.fillStyle = halo
      ctx.fillRect(-6, -6, 12, 12)
      ctx.fillStyle = `rgba(${color},${alpha * 0.65})`
      ctx.beginPath()
      ctx.arc(0, 0, 1.2, 0, Math.PI * 2)
      ctx.fill()
      ctx.restore()
    }

    const draw = () => {
      ctx.clearRect(0, 0, W, H)
      const k = LEVEL * (theme === 'night' ? 0.55 : 1)
      clouds.forEach((c, i) => {
        // 두 층이 서로 다른 방향으로 흐르고, 조금씩 돌고 커졌다 작아진다
        const phase = i * 2.3, direction = i === 0 ? 1 : -1
        const scale = 1.46 + 0.085 * Math.sin(time / 13.103448 + phase)
        ctx.save()
        ctx.globalAlpha = Math.min(1, k * (i === 0 ? 0.9 : 0.65))
        ctx.translate(W * (0.5 + 0.1125 * Math.sin((direction * time) / 13.939394 + phase)), H * (0.5 + 0.0825 * Math.cos(time / 17.952381 + phase)))
        ctx.rotate(0.0775 * Math.sin((direction * time) / 21.106383 + phase))
        ctx.drawImage(c, (-W * scale) / 2, (-H * scale) / 2, W * scale, H * scale)
        ctx.restore()
      })
      ctx.globalAlpha = 1
      if (!reduce.matches) drawComet(k)
      for (const s of stars) {
        ctx.fillStyle = `rgba(${color},${s.a * k})`
        ctx.beginPath()
        ctx.arc(s.x * W, s.y * H, s.r, 0, Math.PI * 2)
        ctx.fill()
      }
      if (!reduce.matches && time >= nextStar) {
        if (glints.length < 6) glints.push({ x: rand(0.06, 0.94), y: rand(0.08, 0.92), start: time, duration: rand(5, 9), r: rand(0.85, 1.3) })
        nextStar = time + rand(1.4, 2.6)
      }
      glints = glints.filter((s) => time - s.start < s.duration)
      for (const s of glints) {
        const a = Math.pow(Math.sin((Math.PI * (time - s.start)) / s.duration), 2) * k
        const x = s.x * W, y = s.y * H
        const g = ctx.createRadialGradient(x, y, 0, x, y, 9)
        g.addColorStop(0, `rgba(${color},${a * 0.18})`)
        g.addColorStop(1, `rgba(${color},0)`)
        ctx.fillStyle = g
        ctx.fillRect(x - 9, y - 9, 18, 18)
        ctx.fillStyle = `rgba(${color},${a * 0.65})`
        ctx.beginPath()
        ctx.arc(x, y, s.r, 0, Math.PI * 2)
        ctx.fill()
      }
    }

    const resize = () => {
      W = window.innerWidth
      H = window.innerHeight
      const d = Math.min(window.devicePixelRatio || 1, 1.5)
      cv.width = Math.round(W * d)
      cv.height = Math.round(H * d)
      ctx.setTransform(d, 0, 0, d, 0, 0)
      stars = Array.from({ length: Math.min(130, Math.round((W * H) / 15000)) }, () => ({ x: Math.random(), y: Math.random(), r: rand(0.45, 1), a: rand(0.08, 0.25) }))
      glints = []
      nextStar = time + 2
      draw()
    }

    const loop = (now: number) => {
      raf = requestAnimationFrame(loop)
      if (now - previous < 1000 / 30) return
      time += Math.min((now - previous) / 1000, 0.1)
      previous = now
      draw()
    }
    const schedule = () => {
      cancelAnimationFrame(raf)
      previous = performance.now()
      if (!document.hidden && !reduce.matches) raf = requestAnimationFrame(loop)
      draw()
    }

    // 성운 무늬는 무거워(수백 ms) 첫 화면이 그려진 뒤에 만들고, 다 되면 천천히 나타난다
    let paint = 0
    const makeClouds = () => {
      window.clearTimeout(paint)
      paint = window.setTimeout(() => {
        color = STAR[theme]
        clouds = [nebula(0, theme), nebula(1, theme)]
        cv.dataset.ready = 'true'
        draw()
      }, 300)
    }
    const themeWatch = new MutationObserver(() => {
      if (themeOf() === theme) return
      theme = themeOf()
      color = STAR[theme]
      makeClouds()
      draw()
    })
    themeWatch.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    const onMotion = () => {
      glints = []
      schedule()
    }

    window.addEventListener('resize', resize)
    document.addEventListener('visibilitychange', schedule)
    reduce.addEventListener('change', onMotion)
    resize()
    makeClouds()
    schedule()
    return () => {
      cancelAnimationFrame(raf)
      window.clearTimeout(paint)
      themeWatch.disconnect()
      window.removeEventListener('resize', resize)
      document.removeEventListener('visibilitychange', schedule)
      reduce.removeEventListener('change', onMotion)
    }
  }, [])

  return <canvas ref={ref} className={styles.sky} aria-hidden="true" />
}
