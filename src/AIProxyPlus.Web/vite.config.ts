/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev-time API port from AIProxyPlus.Api/Properties/launchSettings.json's "http" profile.
const apiDevServerUrl = 'http://localhost:5205'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    // Single-container pattern (specs/iterations/iteration-3/tasks.md Phase 2): `npm run build` writes
    // straight into the ASP.NET project's wwwroot, which ships as-is in the published/container image —
    // no separate copy step needed.
    outDir: '../AIProxyPlus.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': apiDevServerUrl,
      '/signin-oidc': apiDevServerUrl,
      '/signout-callback-oidc': apiDevServerUrl,
      // ws: true — /openai/v1/realtime is a WebSocket upgrade (architecture.md §6.5/§9).
      '/openai': { target: apiDevServerUrl, ws: true },
      '/anthropic': apiDevServerUrl,
      '/health': apiDevServerUrl,
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // FluentUI's Dialog/Tabster focus-trap timing gets flaky under concurrent test-file load (repo memory
    // has the full story) — running files sequentially removes the contention instead of chasing timing bugs.
    fileParallelism: false,
  },
})
