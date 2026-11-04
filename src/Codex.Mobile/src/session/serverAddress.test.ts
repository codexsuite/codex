import { describe, expect, it } from 'vitest';
import { normalizeServerAddress, serverHost } from './serverAddress';

const origin = (input: string) => {
  const result = normalizeServerAddress(input);
  if (!result.ok) throw new Error(`expected ok, got: ${result.error}`);
  return result.origin;
};

describe('normalizeServerAddress', () => {
  it.each([
    ['codex.example.com', 'https://codex.example.com'],
    ['  codex.example.com  ', 'https://codex.example.com'],
    ['CODEX.Example.com', 'https://codex.example.com'],
    ['https://codex.example.com', 'https://codex.example.com'],
    ['HTTPS://codex.example.com', 'https://codex.example.com'],
    ['codex.example.com:8443', 'https://codex.example.com:8443'],
    ['localhost:5688', 'https://localhost:5688'],
  ])('adds https when there is no scheme: %s', (input, expected) => {
    expect(origin(input)).toBe(expected);
  });

  it.each([
    ['http://10.0.2.2:5688', 'http://10.0.2.2:5688'],
    ['http://192.168.1.20:5688/', 'http://192.168.1.20:5688'],
    ['http://localhost:5688', 'http://localhost:5688'],
    ['http://codex.local', 'http://codex.local'],
  ])('keeps http when typed: %s', (input, expected) => {
    expect(origin(input)).toBe(expected);
  });

  it.each([
    ['https://codex.example.com/', 'https://codex.example.com'],
    ['https://codex.example.com/library/books?x=1#top', 'https://codex.example.com'],
    ['codex.example.com/login', 'https://codex.example.com'],
    ['codex.example.com?next=/a', 'https://codex.example.com'],
    ['codex.example.com#frag', 'https://codex.example.com'],
    ['http://10.0.2.2:5688/api/auth/me', 'http://10.0.2.2:5688'],
  ])('reduces to the origin: %s', (input, expected) => {
    expect(origin(input)).toBe(expected);
  });

  it('drops a default port', () => {
    expect(origin('https://codex.example.com:443')).toBe('https://codex.example.com');
    expect(origin('http://codex.example.com:80')).toBe('http://codex.example.com');
  });

  it('accepts an IPv6 host', () => {
    expect(origin('http://[::1]:5688')).toBe('http://[::1]:5688');
  });

  it.each(['', '   ', '\t\n'])('rejects empty input: %j', (input) => {
    const result = normalizeServerAddress(input);
    expect(result).toEqual({ ok: false, error: 'Enter the address of your Codex server.' });
  });

  it.each([
    'ftp://codex.example.com',
    'file:///etc/passwd',
    'javascript://x',
    'ws://codex.example.com',
  ])('rejects another scheme: %s', (input) => {
    const result = normalizeServerAddress(input);
    expect(result).toEqual({
      ok: false,
      error: 'The address must start with http:// or https://.',
    });
  });

  it.each([
    'user:pass@codex.example.com',
    'https://user:pass@codex.example.com',
    'https://user@codex.example.com',
  ])('rejects credentials in the address: %s', (input) => {
    const result = normalizeServerAddress(input);
    expect(result).toEqual({
      ok: false,
      error: 'Leave the user name and password out of the address.',
    });
  });

  it.each([
    'http://',
    'https://:8080',
    'a b.com',
    'codex.example.com:99999',
    'mailto:me@example.com',
    'https://',
  ])('rejects what URL cannot parse: %s', (input) => {
    const result = normalizeServerAddress(input);
    expect(result.ok).toBe(false);
  });
});

describe('serverHost', () => {
  it('drops the scheme and keeps the port', () => {
    expect(serverHost('https://codex.example.com')).toBe('codex.example.com');
    expect(serverHost('http://10.0.2.2:5688')).toBe('10.0.2.2:5688');
  });
});
