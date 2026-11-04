import { describe, expect, it, vi } from 'vitest';
import { createSessionStore, EMAIL_KEY, SERVER_KEY, type SessionPersistence } from './sessionStore';

function fakePersistence(initial: { kv?: Record<string, string>; secret?: string } = {}) {
  const kv = new Map(Object.entries(initial.kv ?? {}));
  let secret: string | null = initial.secret ?? null;
  const persistence: SessionPersistence = {
    readKv: (key) => kv.get(key) ?? null,
    writeKv: (key, value) => void kv.set(key, value),
    readSecret: () => secret,
    writeSecret: (value) => void (secret = value),
    deleteSecret: async () => void (secret = null),
  };
  return { persistence, kv, secret: () => secret };
}

describe('createSessionStore', () => {
  it('loads server, email and secret from storage', () => {
    const { persistence } = fakePersistence({
      kv: { [SERVER_KEY]: 'https://codex.example.com', [EMAIL_KEY]: 'jd@example.com' },
      secret: 'cdx_dev_abc',
    });
    const store = createSessionStore(persistence);
    expect(store.getState()).toEqual({
      origin: 'https://codex.example.com',
      email: 'jd@example.com',
      secret: 'cdx_dev_abc',
    });
    expect(store.credentials()).toEqual({
      origin: 'https://codex.example.com',
      secret: 'cdx_dev_abc',
    });
  });

  it('starts empty and survives storage that throws', () => {
    const broken: SessionPersistence = {
      readKv: () => {
        throw new Error('blocked');
      },
      writeKv: () => undefined,
      readSecret: () => {
        throw new Error('blocked');
      },
      writeSecret: () => undefined,
      deleteSecret: async () => undefined,
    };
    expect(createSessionStore(broken).getState()).toEqual({
      origin: null,
      email: '',
      secret: null,
    });
  });

  it('sends requests to an attempt without counting as signed in', () => {
    const { persistence } = fakePersistence();
    const store = createSessionStore(persistence);
    const listener = vi.fn();
    store.subscribe(listener);

    store.startAttempt('http://10.0.2.2:5688');
    store.setAttemptSecret('cdx_dev_new');
    expect(store.credentials()).toEqual({ origin: 'http://10.0.2.2:5688', secret: 'cdx_dev_new' });
    expect(store.getState().secret).toBeNull();
    expect(listener).not.toHaveBeenCalled();

    store.endAttempt();
    expect(store.credentials()).toEqual({ origin: null, secret: null });
  });

  it('commits an attempt: persists all three and notifies', () => {
    const { persistence, kv, secret } = fakePersistence();
    const store = createSessionStore(persistence);
    const listener = vi.fn();
    store.subscribe(listener);

    store.startAttempt('https://codex.example.com');
    store.setAttemptSecret('cdx_dev_new');
    store.commitAttempt('jd@example.com');

    expect(store.getState()).toEqual({
      origin: 'https://codex.example.com',
      email: 'jd@example.com',
      secret: 'cdx_dev_new',
    });
    expect(secret()).toBe('cdx_dev_new');
    expect(kv.get(SERVER_KEY)).toBe('https://codex.example.com');
    expect(kv.get(EMAIL_KEY)).toBe('jd@example.com');
    expect([...kv.values()]).not.toContain('cdx_dev_new');
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('refuses to commit without a secret', () => {
    const store = createSessionStore(fakePersistence().persistence);
    store.startAttempt('https://codex.example.com');
    expect(() => store.commitAttempt('a@b.c')).toThrow();
  });

  it('clears the secret and keeps server and email', async () => {
    const { persistence, kv, secret } = fakePersistence({
      kv: { [SERVER_KEY]: 'https://codex.example.com', [EMAIL_KEY]: 'jd@example.com' },
      secret: 'cdx_dev_abc',
    });
    const store = createSessionStore(persistence);
    const listener = vi.fn();
    store.subscribe(listener);

    await store.clearSecret();

    expect(store.getState()).toEqual({
      origin: 'https://codex.example.com',
      email: 'jd@example.com',
      secret: null,
    });
    expect(secret()).toBeNull();
    expect(kv.get(SERVER_KEY)).toBe('https://codex.example.com');
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('clears in memory even when deleting the stored secret fails', async () => {
    const { persistence } = fakePersistence({ secret: 'cdx_dev_abc' });
    persistence.deleteSecret = () => Promise.reject(new Error('keychain locked'));
    const store = createSessionStore(persistence);
    await store.clearSecret();
    expect(store.getState().secret).toBeNull();
  });
});
