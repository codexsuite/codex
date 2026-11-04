import { ApiRequestError } from '@codex/core/api/client';
import type { CreatedDeviceSession, Me } from '@codex/core/api/auth';
import { describe, expect, it, vi } from 'vitest';
import { createSessionStore, type SessionPersistence } from './sessionStore';
import {
  MUST_CHANGE_PASSWORD_MESSAGE,
  lostSession,
  signIn,
  signOut,
  signInErrorMessage,
} from './signIn';

const me = (overrides: Partial<Me> = {}): Me => ({
  id: 'u1',
  email: 'jd@example.com',
  displayName: 'JD',
  initials: 'JD',
  isAdmin: false,
  mustChangePassword: false,
  ...overrides,
});

function setup(options: { existingSecret?: string; me?: Me } = {}) {
  let secret: string | null = options.existingSecret ?? null;
  const kv = new Map<string, string>();
  const persistence: SessionPersistence = {
    readKv: (key) => kv.get(key) ?? null,
    writeKv: (key, value) => void kv.set(key, value),
    readSecret: () => secret,
    writeSecret: (value) => void (secret = value),
    deleteSecret: async () => void (secret = null),
  };
  const store = createSessionStore(persistence);
  const seen: { origin: string | null; secret: string | null }[] = [];
  const deps = {
    store,
    createDeviceSession: vi.fn(async (): Promise<CreatedDeviceSession> => {
      seen.push(store.credentials());
      return {
        session: {} as CreatedDeviceSession['session'],
        secret: 'cdx_dev_new',
        me: options.me ?? me(),
      };
    }),
    endCurrentDeviceSession: vi.fn(async () => {
      seen.push(store.credentials());
    }),
    deviceName: () => 'Pixel',
    seedMe: vi.fn(),
    clearCache: vi.fn(),
  };
  return { deps, store, seen, secret: () => secret };
}

describe('signIn', () => {
  it('rejects a bad address without calling the server', async () => {
    const { deps } = setup();
    const result = await signIn(deps, { server: 'ftp://x', email: 'a@b.c', password: 'p' });
    expect(result.ok).toBe(false);
    expect(deps.createDeviceSession).not.toHaveBeenCalled();
  });

  it('signs in against the normalised origin, stores the secret and seeds me', async () => {
    const { deps, store, seen, secret } = setup();
    const result = await signIn(deps, {
      server: 'http://10.0.2.2:5688/',
      email: ' jd@example.com ',
      password: 'pw',
    });

    expect(result).toEqual({ ok: true });
    expect(deps.createDeviceSession).toHaveBeenCalledWith('jd@example.com', 'pw', 'Pixel');
    expect(seen[0]).toEqual({ origin: 'http://10.0.2.2:5688', secret: null });
    expect(store.getState()).toEqual({
      origin: 'http://10.0.2.2:5688',
      email: 'jd@example.com',
      secret: 'cdx_dev_new',
    });
    expect(secret()).toBe('cdx_dev_new');
    expect(deps.seedMe).toHaveBeenCalledWith(me());
  });

  it('does not enter the app when the password must change, and ends the new session', async () => {
    const { deps, store, seen, secret } = setup({ me: me({ mustChangePassword: true }) });
    const result = await signIn(deps, {
      server: 'codex.example.com',
      email: 'a@b.c',
      password: 'p',
    });

    expect(result).toEqual({ ok: false, error: MUST_CHANGE_PASSWORD_MESSAGE });
    expect(deps.endCurrentDeviceSession).toHaveBeenCalledTimes(1);
    expect(seen[1]).toEqual({ origin: 'https://codex.example.com', secret: 'cdx_dev_new' });
    expect(store.getState().secret).toBeNull();
    expect(secret()).toBeNull();
    expect(deps.seedMe).not.toHaveBeenCalled();
  });

  it('shows the server sentence for a 401', async () => {
    const { deps, store } = setup();
    deps.createDeviceSession.mockRejectedValue(
      new ApiRequestError('Email or password is wrong.', 401),
    );
    const result = await signIn(deps, {
      server: 'codex.example.com',
      email: 'a@b.c',
      password: 'p',
    });
    expect(result).toEqual({ ok: false, error: 'Email or password is wrong.' });
    expect(store.credentials()).toEqual({ origin: null, secret: null });
  });

  it('keeps the previous session untouched when a new sign-in fails', async () => {
    const { deps, store } = setup({ existingSecret: 'cdx_dev_old' });
    deps.createDeviceSession.mockRejectedValue(new TypeError('Network request failed'));
    await signIn(deps, { server: 'codex.example.com', email: 'a@b.c', password: 'p' });
    expect(store.credentials().secret).toBe('cdx_dev_old');
  });
});

describe('signInErrorMessage', () => {
  it.each([
    [
      new ApiRequestError('Too many attempts. Try again in 5 minutes.', 429),
      'Too many attempts. Try again in 5 minutes.',
    ],
    [new ApiRequestError('boom', 500), 'Sign-in failed (500). Try again.'],
    [new ApiRequestError('Bad name', 400), 'Sign-in failed (400). Try again.'],
    [
      new TypeError('Network request failed'),
      "Couldn't reach codex.example.com. Check the address and your connection.",
    ],
    [new Error('other'), 'Sign-in failed. Try again.'],
  ])('maps %s', (error, expected) => {
    expect(signInErrorMessage(error, 'codex.example.com')).toBe(expected);
  });
});

describe('signOut', () => {
  it('ends the session on the server and clears locally', async () => {
    const { deps, store, secret } = setup({ existingSecret: 'cdx_dev_old' });
    await signOut(deps);
    expect(deps.endCurrentDeviceSession).toHaveBeenCalledTimes(1);
    expect(store.getState().secret).toBeNull();
    expect(secret()).toBeNull();
    expect(deps.clearCache).toHaveBeenCalled();
  });

  it('clears locally when the server cannot be reached', async () => {
    const { deps, store } = setup({ existingSecret: 'cdx_dev_old' });
    deps.endCurrentDeviceSession.mockRejectedValue(new TypeError('offline'));
    await signOut(deps);
    expect(store.getState().secret).toBeNull();
  });
});

describe('lostSession', () => {
  it('clears the secret and keeps the server and email', async () => {
    const { deps, store } = setup();
    await signIn(deps, { server: 'codex.example.com', email: 'jd@example.com', password: 'p' });
    await lostSession(deps);
    expect(store.getState()).toEqual({
      origin: 'https://codex.example.com',
      email: 'jd@example.com',
      secret: null,
    });
  });
});
