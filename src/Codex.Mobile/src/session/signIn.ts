import { ApiRequestError } from '@codex/core/api/client';
import type { CreatedDeviceSession, Me } from '@codex/core/api/auth';
import { normalizeServerAddress, serverHost } from './serverAddress';
import type { SessionStore } from './sessionStore';

// Sign-in and sign-out for the app (#35). The network calls and the query cache are passed in, so vitest covers the
// flow without Expo; `auth.ts` binds the real ones.

export interface SignInDeps {
  store: SessionStore;
  createDeviceSession: (
    email: string,
    password: string,
    deviceName: string,
  ) => Promise<CreatedDeviceSession>;
  endCurrentDeviceSession: () => Promise<void>;
  deviceName: () => string;
  /** Puts `me` in the query cache under `meKey`. */
  seedMe: (me: Me) => void;
  /** Empties the query cache (the next user must not see this one's data). */
  clearCache: () => void;
}

export type SignInResult = { ok: true } | { ok: false; error: string };

export const MUST_CHANGE_PASSWORD_MESSAGE =
  'Your password must be changed first. Sign in to Codex on the web to set a new one.';

export async function signIn(
  deps: SignInDeps,
  input: { server: string; email: string; password: string },
): Promise<SignInResult> {
  const address = normalizeServerAddress(input.server);
  if (!address.ok) return { ok: false, error: address.error };

  const email = input.email.trim();
  deps.store.startAttempt(address.origin);
  try {
    const created = await deps.createDeviceSession(email, input.password, deps.deviceName());
    deps.store.setAttemptSecret(created.secret);

    if (created.me.mustChangePassword) {
      // Don't enter the app: end the session we just made, with its own secret.
      await deps.endCurrentDeviceSession().catch(() => undefined);
      return { ok: false, error: MUST_CHANGE_PASSWORD_MESSAGE };
    }

    deps.clearCache();
    deps.store.commitAttempt(email);
    deps.seedMe(created.me);
    return { ok: true };
  } catch (error) {
    return { ok: false, error: signInErrorMessage(error, serverHost(address.origin)) };
  } finally {
    deps.store.endAttempt();
  }
}

/** Ends the session on the server when it can, and clears it here whatever the server answers (offline included). */
export async function signOut(
  deps: Pick<SignInDeps, 'store' | 'endCurrentDeviceSession' | 'clearCache'>,
): Promise<void> {
  try {
    await deps.endCurrentDeviceSession();
  } catch {
    // Offline, or the session was already gone: sign out locally anyway.
  }
  await deps.store.clearSecret();
  deps.clearCache();
}

/** A request answered 401 (password changed elsewhere, session ended): back to sign-in with server and email kept. */
export async function lostSession(deps: Pick<SignInDeps, 'store' | 'clearCache'>): Promise<void> {
  deps.clearCache();
  await deps.store.clearSecret();
}

export function signInErrorMessage(error: unknown, host: string): string {
  if (error instanceof ApiRequestError) {
    if (error.status === 401 || error.status === 429) return error.message;
    return `Sign-in failed (${error.status}). Try again.`;
  }
  if (error instanceof TypeError) {
    return `Couldn't reach ${host}. Check the address and your connection.`;
  }
  return 'Sign-in failed. Try again.';
}
