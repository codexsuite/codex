import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetch } from '../test-support/stubFetch';
import { createDeviceSession, endCurrentDeviceSession, loginLink, safeReturnUrl } from './auth';
import { api, session } from './client';

describe('safeReturnUrl', () => {
  it.each([
    ['/books/1/plan?view=board#scene=5.2', '/books/1/plan?view=board#scene=5.2'],
    ['/', '/'],
    [null, '/'],
    ['https://evil.example', '/'],
    ['//evil.example/path', '/'],
    ['/\\evil.example', '/'],
    ['javascript:alert(1)', '/'],
    ['books/1', '/'],
  ])('follows %s as %s', (value, expected) => {
    expect(safeReturnUrl(value)).toBe(expected);
  });

  it('builds the sign-in link with the return path encoded', () => {
    expect(loginLink('/')).toBe('/login');
    expect(loginLink('/books/1?x=1')).toBe('/login?returnUrl=%2Fbooks%2F1%3Fx%3D1');
  });
});

describe('a lost session', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('sends the page to sign in on any 401', async () => {
    const lost = vi.spyOn(session, 'onUnauthorized').mockImplementation(() => {});
    stubFetch(401, { error: 'Sign in to continue.', code: 'unauthorized' });

    await expect(api.get('/api/library')).rejects.toMatchObject({ status: 401 });

    expect(lost).toHaveBeenCalledOnce();
  });

  it('leaves a wrong password on the sign-in call to the sign-in page', async () => {
    const lost = vi.spyOn(session, 'onUnauthorized').mockImplementation(() => {});
    stubFetch(401, { error: 'Incorrect email or password.', code: 'unauthorized' });

    await expect(api.post('/api/auth/login', { email: 'a', password: 'b' })).rejects.toThrow(
      'Incorrect email or password.',
    );

    expect(lost).not.toHaveBeenCalled();
  });

  it('leaves a wrong password on the device sign-in to the sign-in screen', async () => {
    const lost = vi.spyOn(session, 'onUnauthorized').mockImplementation(() => {});
    stubFetch(401, { error: 'Incorrect email or password.', code: 'unauthorized' });

    await expect(createDeviceSession('a', 'b', 'Phone')).rejects.toMatchObject({ status: 401 });

    expect(lost).not.toHaveBeenCalled();
  });

  it('treats a 401 on ending the device session as a lost session', async () => {
    const lost = vi.spyOn(session, 'onUnauthorized').mockImplementation(() => {});
    stubFetch(401, { error: 'Sign in to continue.', code: 'unauthorized' });

    await expect(endCurrentDeviceSession()).rejects.toMatchObject({ status: 401 });

    expect(lost).toHaveBeenCalledOnce();
  });

  it('does not treat a 403 or 404 as a lost session', async () => {
    const lost = vi.spyOn(session, 'onUnauthorized').mockImplementation(() => {});
    stubFetch(403, { error: 'Only admins can manage users.', code: 'forbidden' });

    await expect(api.get('/api/admin/users')).rejects.toMatchObject({ status: 403 });

    expect(lost).not.toHaveBeenCalled();
  });
});
