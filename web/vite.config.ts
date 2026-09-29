import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// 빌드 결과는 코어 서버가 제공한다. 개발 중(npm run dev)에는 /api를 코어 서버로 넘긴다.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/Astro.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: { '/api': 'http://localhost:5210' },
  },
})
