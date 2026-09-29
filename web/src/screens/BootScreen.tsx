import styles from './BootScreen.module.css'

export default function BootScreen({ error }: { error?: string | null }) {
  return (
    <main className={styles.boot} aria-label="AA를 시작하는 중">
      <p className={styles.mark}>AA</p>
      <p className={styles.name}>천체사진 촬영 비서</p>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </main>
  )
}
