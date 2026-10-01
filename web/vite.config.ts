import react from '@vitejs/plugin-react'
import { execSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'

// 제품 이름은 저장소 맨 위의 product.json 한 곳에서만 정한다 (.NET 쪽도 같은 파일을 읽는다).
const product = JSON.parse(readFileSync(new URL('../product.json', import.meta.url), 'utf8'))

// 빌드 번호 = 저장소의 커밋 수 (집·회사 PC와 Codex가 같은 저장소를 쓰므로, 어느 PC에서 빌드해도 같은 커밋이면 같은 번호).
// 버전 표기: v + product.json의 version + . + 빌드 번호 (예: v0.0.27). git이 없으면 0
function buildNumber() {
  try {
    return execSync('git rev-list --count HEAD', { cwd: new URL('..', import.meta.url) }).toString().trim()
  } catch {
    return '0'
  }
}
const version = `v${product.version}.${buildNumber()}`

// 빌드 결과는 코어 서버가 제공한다. 개발 중(npm run dev)에는 /api를 코어 서버로 넘긴다.
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'product-name-in-html',
      transformIndexHtml: (html) => html.replaceAll('%PRODUCT_NAME%', product.name),
    },
  ],
  define: {
    __PRODUCT__: JSON.stringify(product),
    __VERSION__: JSON.stringify(version),
  },
  build: {
    outDir: '../src/Astro.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: { '/api': 'http://localhost:5210' },
  },
})
