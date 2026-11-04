import type { Platform } from '@codex/core/platform';

// The browser behind the core's platform adapter (#35): Web Storage, `navigator.onLine` and the sign-in redirect.
// Requests go to this page's origin and the cookie signs them in, so there is no origin or header to add.
// Configured before anything renders: `main.tsx`, `playwright/index.tsx` and the vitest setup.
export const webPlatform: Platform = {
  persistentStore: () => window.localStorage,
  sessionStore: () => window.sessionStorage,
  isOffline: () => navigator.onLine === false,
  onOnline(listener) {
    window.addEventListener('online', listener);
    return () => window.removeEventListener('online', listener);
  },
  // To the sign-in page, which brings the user back here afterwards; not when already on it.
  onUnauthorized() {
    if (window.location.pathname === '/login') return;
    const here = window.location.pathname + window.location.search + window.location.hash;
    window.location.assign(
      here === '/' ? '/login' : `/login?returnUrl=${encodeURIComponent(here)}`,
    );
  },
  apiOrigin: () => '',
  authHeaders: () => ({}),
};
