/**
 * 화면 기록 (2026-10-10 사용자 요청 — 완성 전까지 모든 단계에 로그). 서버 기록 파일(logs/aira-날짜.log)에 "화면"으로 남는다.
 * 보내고 기다리지 않는다 — 기록이 실패해도 화면은 그대로
 */
export function logEvent(message: string) {
  void fetch('/api/log', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message }),
    keepalive: true,
  }).catch(() => null)
}

/** 누른 버튼을 모두 기록 (이름표 또는 글자, 40자까지). 앱이 한 번 켠다 */
export function logClicks(): () => void {
  const onClick = (e: MouseEvent) => {
    const el = (e.target as Element | null)?.closest('button, [role="button"], a')
    if (!el) return
    const label = (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 40)
    if (label) logEvent(`누름: ${label}`)
  }
  document.addEventListener('click', onClick, true)
  return () => document.removeEventListener('click', onClick, true)
}
