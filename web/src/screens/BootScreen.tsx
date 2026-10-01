import { PRODUCT, VERSION } from '../product'
import styles from './BootScreen.module.css'

export default function BootScreen({ error }: { error?: string | null }) {
  return (
    <main className={styles.boot} aria-label={`${PRODUCT.reul} 시작하는 중`}>
      <p className={styles.mark}>{PRODUCT.name}</p>
      <p className={styles.name}>{PRODUCT.tagline}</p>
      <p className={styles.version}>{VERSION}</p>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </main>
  )
}
