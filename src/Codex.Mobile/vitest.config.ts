import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

// Plain Node: the tests cover the app's pure modules (session, server address, theme, links), not React Native views.
// Screens and primitives are `*.spec.{ts,tsx}`, run by jest-expo (`pnpm run test:screens`); the two patterns never overlap.
export default defineConfig({
  resolve: { alias: { '~': fileURLToPath(new URL('./src', import.meta.url)) } },
  test: { include: ['src/**/*.test.ts'] },
});
