// What the core needs from the app it runs in (#35). The web SPA (`webPlatform`) and the mobile app each configure one
// at startup, before anything reads storage or sends a request. Until then the core runs on in-memory storage, online,
// with nothing to do when a session is lost: enough for the core's own tests.

/** A synchronous string store with the Web Storage shape. It may throw (storage blocked or full): callers guard. */
export interface KeyValueStore {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface Platform {
  /** Kept across restarts (web: `localStorage`). A function, because reaching the store can itself throw. */
  persistentStore(): KeyValueStore;
  /** Kept for this tab or app session only (web: `sessionStorage`). */
  sessionStore(): KeyValueStore;
  /** True when the device knows it has no connection. */
  isOffline(): boolean;
  /** Calls `listener` each time the connection comes back; returns the unsubscribe. */
  onOnline(listener: () => void): () => void;
  /** A request other than sign-in answered 401: the session is gone (web: go to the sign-in page). */
  onUnauthorized(): void;
  /**
   * Where `/api/...` paths are sent: '' on the web (relative to the page), the server the user signed in to on mobile
   * (`https://codex.example.com`, no trailing slash).
   */
  apiOrigin(): string;
  /** Headers every request carries besides the core's own (mobile: `Authorization: Bearer cdx_dev_…`; web: none). */
  authHeaders(): Record<string, string>;
}

export function memoryStore(): KeyValueStore {
  const values = new Map<string, string>();
  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => void values.set(key, value),
    removeItem: (key) => void values.delete(key),
  };
}

function memoryPlatform(): Platform {
  const persistent = memoryStore();
  const session = memoryStore();
  return {
    persistentStore: () => persistent,
    sessionStore: () => session,
    isOffline: () => false,
    onOnline: () => () => {},
    onUnauthorized: () => {},
    apiOrigin: () => '',
    authHeaders: () => ({}),
  };
}

let current: Platform = memoryPlatform();

export function configurePlatform(next: Platform) {
  current = next;
}

export function platform(): Platform {
  return current;
}
