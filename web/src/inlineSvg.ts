/**
 * 그림 파일(SVG)을 글자 그대로 화면에 넣을 때(테마 색을 바꾸려고 img 대신 인라인 — 로고·상태 줄 별자리 글자),
 * 그 안의 <style>과 id가 문서 전체에 퍼지지 않게 그 그림 안으로 가둔다.
 * 2026-10-11 버그: 로고 안의 `svg{color:var(--ink);--accent:…}`가 앱의 모든 아이콘에 걸려 다크 테마에서도 아이콘이 연보라로 보였다.
 * - 바깥 svg에 scope 클래스를 붙이고, 스타일 선택자를 모두 `.scope …`로 바꾼다 (`svg{…}` → `.scope{…}`)
 * - id는 `scope-이름`으로 바꾸고 url(#…)·href·선택자의 #…도 같이 (같은 그림이 둘 있어도 섞이지 않게)
 * - data-theme은 지금 앱 테마로
 */
export function scopedSvg(raw: string, scope: string, theme: string): string {
  const ids = [...raw.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1])
  let out = raw.replace(/data-theme="[^"]*"/, `data-theme="${theme}"`).replace(/<svg\b/, `<svg class="${scope}"`)
  for (const id of ids) {
    const esc = id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    out = out
      .replace(new RegExp(`\\sid="${esc}"`, 'g'), ` id="${scope}-${id}"`)
      .replace(new RegExp(`url\\(#${esc}\\)`, 'g'), `url(#${scope}-${id})`)
      .replace(new RegExp(`href="#${esc}"`, 'g'), `href="#${scope}-${id}"`)
  }
  return out.replace(/<style>([\s\S]*?)<\/style>/g, (_, css: string) => {
    const scoped = css
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/([^{}]+)\{/g, (_m, selectors: string) => {
        const list = selectors
          .split(',')
          .map((s) => s.trim())
          .filter(Boolean)
          .map((s) => {
            const withIds = ids.reduce((acc, id) => acc.replace(new RegExp(`#${id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`, 'g'), `#${scope}-${id}`), s)
            return withIds.startsWith('svg') ? `.${scope}${withIds.slice(3)}` : `.${scope} ${withIds}`
          })
        return `${list.join(',')}{`
      })
    return `<style>${scoped}</style>`
  })
}
