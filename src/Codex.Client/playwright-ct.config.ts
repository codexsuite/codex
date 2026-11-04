import tailwindcss from '@tailwindcss/vite';
import { defineConfig, devices } from '@playwright/experimental-ct-react';

export default defineConfig({
  testDir: './src',
  testMatch: '**/*.spec.tsx',
  timeout: 30_000,
  reporter: [['list'], ['html', { open: 'never' }]],
  expect: {
    // On CI runners the first mount in a worker is cold (Vite bundles the screen's chunks on demand) and
    // the machine is slower, so a correct screen can take longer than 5 s to appear (Plan.spec's first card test
    // rendered at ~11 s in wave 3). Locally the default stays, so a slow screen is still noticed.
    timeout: process.env.CI ? 15_000 : 5_000,
    // Baselines are generated inside the same Docker image CI runs in, so
    // renders are near-deterministic and the budget can be a small absolute
    // one. The old 2% ratio was so lax that a full-page restyle of the
    // sparser (mostly-background) pages still passed against stale baselines.
    toHaveScreenshot: { maxDiffPixels: 250 },
  },
  use: {
    ...devices['Desktop Chrome'],
    timezoneId: 'UTC',
    // Several worktrees run component tests on one host; each picks its own port.
    ctPort: Number(process.env.CODEX_CT_PORT ?? 3100),
    // Playwright CT bundles components with its own internal Vite instance —
    // it does not read plugins from vite.config.ts, so Tailwind has to be
    // registered here too or component screenshots render unstyled.
    ctViteConfig: {
      plugins: [tailwindcss()],
    },
  },
});
