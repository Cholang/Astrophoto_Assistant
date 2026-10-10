import logo from '../assets/aira-logo.svg?raw'
import { PRODUCT, VERSION } from '../product'
import styles from './BootScreen.module.css'

/**
 * 부팅 화면: 로고(2026-10-10 사용자 로고 mockups/aira-brand/aira-logo-v2.svg — 한글 이름·영어 풀이가 그림 안에 있음) + 버전 (한 줄 설명은 2026-10-10 사용자 요청으로 뺌).
 * 로고 SVG는 data-theme(dark·light·night)으로 색이 바뀌어서, 지금 앱 테마를 넣어 그 자리에 그린다 (img로 넣으면 테마가 안 먹음)
 */
export default function BootScreen({ error }: { error?: string | null }) {
  const theme = document.documentElement.dataset.theme ?? 'dark'
  const svg = logo.replace(/data-theme="[^"]*"/, `data-theme="${theme}"`)
  return (
    <main className={styles.boot} aria-label={`${PRODUCT.reul} 시작하는 중`}>
      <div className={styles.logo} dangerouslySetInnerHTML={{ __html: svg }} />
      <p className={styles.version}>{VERSION}</p>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </main>
  )
}
