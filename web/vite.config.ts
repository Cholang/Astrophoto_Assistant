import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'

// 제품 이름은 저장소 맨 위의 product.json 한 곳에서만 정한다 (.NET 쪽도 같은 파일을 읽는다).
const product = JSON.parse(readFileSync(new URL('../product.json', import.meta.url), 'utf8'))

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
  },
  build: {
    outDir: '../src/Astro.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: { '/api': 'http://localhost:5210' },
  },
})
