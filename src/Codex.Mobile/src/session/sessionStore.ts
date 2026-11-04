// The session the app holds in memory (#35): the server, the signed-in user's email and the device secret.
// `apiOrigin()` and `authHeaders()` are synchronous, so everything is read once when the store is created.
// Pure (the storage is passed in), so vitest covers it; `session.ts` binds the Expo storage.

export interface SessionPersistence {
  /** Server and email: non-secret, kept in the key-value store. */
  readKv(key: string): string | null;
  writeKv(key: string, value: string): void;
  /** The device secret: only ever in the secure store (Keychain / Keystore). */
  readSecret(): string | null;
  writeSecret(value: string): void;
  deleteSecret(): Promise<void>;
}

export interface SessionState {
  /** The server the user last signed in to (kept after a sign-out, to prefill the sign-in screen). */
  origin: string | null;
  /** The last email used (kept after a sign-out). */
  email: string;
  /** Set only while signed in. */
  secret: string | null;
}

export const SERVER_KEY = 'codex.server';
export const EMAIL_KEY = 'codex.email';

/** The server and secret requests use: a sign-in attempt in progress takes precedence over the saved session. */
export interface Credentials {
  origin: string | null;
  secret: string | null;
}

export function createSessionStore(persistence: SessionPersistence) {
  let state: SessionState = {
    origin: safe(() => persistence.readKv(SERVER_KEY)),
    email: safe(() => persistence.readKv(EMAIL_KEY)) ?? '',
    secret: safe(() => persistence.readSecret()),
  };
  // A sign-in in flight: requests go to its server (and carry its secret) without the app counting as signed in.
  let attempt: Credentials | null = null;
  const listeners = new Set<() => void>();

  function set(next: SessionState) {
    state = next;
    listeners.forEach((listener) => listener());
  }

  return {
    getState: () => state,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => void listeners.delete(listener);
    },
    credentials: (): Credentials => attempt ?? { origin: state.origin, secret: state.secret },

    startAttempt(origin: string) {
      attempt = { origin, secret: null };
    },
    setAttemptSecret(secret: string) {
      if (attempt) attempt = { ...attempt, secret };
    },
    endAttempt() {
      attempt = null;
    },
    /** The attempt worked: save server, email and secret, and count as signed in. */
    commitAttempt(email: string) {
      if (!attempt?.origin || !attempt.secret) throw new Error('No sign-in attempt to commit.');
      const { origin, secret } = attempt;
      persistence.writeSecret(secret);
      persistence.writeKv(SERVER_KEY, origin);
      persistence.writeKv(EMAIL_KEY, email);
      attempt = null;
      set({ origin, email, secret });
    },
    /** Sign out or a lost session: drop the secret, keep the server and email. */
    async clearSecret() {
      attempt = null;
      if (state.secret !== null) set({ ...state, secret: null });
      try {
        await persistence.deleteSecret();
      } catch {
        // The in-memory session is already gone; a leftover secret is rejected by the server.
      }
    },
  };
}

export type SessionStore = ReturnType<typeof createSessionStore>;

function safe<T>(read: () => T | null): T | null {
  try {
    return read();
  } catch {
    return null;
  }
}
