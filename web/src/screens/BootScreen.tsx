import { PRODUCT, VERSION } from '../product'
import styles from './BootScreen.module.css'

export default function BootScreen({ error }: { error?: string | null }) {
  return (
    <main className={styles.boot} aria-label={`${PRODUCT.reul} 시작하는 중`}>
      <p className={styles.mark}>{PRODUCT.name}</p>
      {/* 영어 풀이 (2026-10-09 사용자 요청): 낱말 첫 글자가 이름(AIRA)이 되는 것이 보이게 첫 글자만 강조 */}
      {PRODUCT.fullName && (
        <p className={styles.full} lang="en">
          {PRODUCT.fullName.split(' ').map((w, i) => (
            <span key={i}>
              {i > 0 && ' '}
              {/^[A-Z]/.test(w) ? (
                <>
                  <b>{w[0]}</b>
                  {w.slice(1)}
                </>
              ) : (
                w
              )}
            </span>
          ))}
        </p>
      )}
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
