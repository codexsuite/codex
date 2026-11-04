/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 3000,
    // The proxy target below must never silently shift ports.
    strictPort: true,
    proxy: {
      // Kestrel's port, from src/Codex.WebApi/Properties/launchSettings.json.
      '/api': { target: 'http://localhost:5688', changeOrigin: true },
      // SignalR hubs (WebSocket upgrade included).
      '/hubs': { target: 'http://localhost:5688', changeOrigin: true, ws: true },
    },
  },
  build: {
    // Straight into the API project: ASP.NET serves what Vite emits, no copy step.
    outDir: '../Codex.WebApi/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      // Two pages in one build: the SPA, and the editor page the mobile app loads into its WebView (served as the
      // static file /mobile-editor.html, #35).
      input: {
        main: resolve(import.meta.dirname, 'index.html'),
        'mobile-editor': resolve(import.meta.dirname, 'mobile-editor.html'),
      },
    },
  },
  test: {
    environment: 'jsdom',
    // *.spec.tsx are Playwright component tests (pnpm run test-ct), not vitest.
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test-support/webPlatformSetup.ts'],
  },
});
