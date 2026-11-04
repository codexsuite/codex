import { describe, expect, it, vi } from 'vitest';
import {
  canSignIn,
  checkLine,
  notCodexMessage,
  runServerCheck,
  unreachableMessage,
} from './serverCheck';

const live = () => new AbortController().signal;

describe('runServerCheck', () => {
  it('ShouldAskTheNormalisedOriginAndReturnOkWithTheVersion', async () => {
    const fetchHealth = vi.fn().mockResolvedValue({ status: 'ok', version: '1.0.0+abc' });
    const checking = vi.fn();

    const result = await runServerCheck('codex.example.com/x', fetchHealth, live(), checking);

    expect(checking).toHaveBeenCalledWith('https://codex.example.com');
    expect(fetchHealth).toHaveBeenCalledWith({
      origin: 'https://codex.example.com',
      signal: expect.any(AbortSignal),
    });
    expect(result).toEqual({
      kind: 'ok',
      origin: 'https://codex.example.com',
      version: '1.0.0+abc',
    });
  });

  it('ShouldFailNamingTheHostWhenTheServerDoesNotAnswer', async () => {
    const fetchHealth = vi.fn().mockRejectedValue(new TypeError('Network request failed'));

    expect(await runServerCheck('codex.exmaple.com:8443', fetchHealth, live())).toEqual({
      kind: 'failed',
      message: unreachableMessage('codex.exmaple.com:8443'),
    });
  });

  it('ShouldFailWithTheAddressMessageAndNotAskForABadAddress', async () => {
    const fetchHealth = vi.fn();

    expect(await runServerCheck('ftp://x', fetchHealth, live())).toEqual({
      kind: 'failed',
      message: 'The address must start with http:// or https://.',
    });
    expect(fetchHealth).not.toHaveBeenCalled();
  });

  it.each([
    ['has no version', { status: 'ok' }],
    ['has a non-string version', { status: 'ok', version: 3 }],
    ['has no status', { version: '1.0.0' }],
    ['has another status', { status: 'degraded', version: '1.0.0' }],
    ['is empty', {}],
  ])('ShouldFailAsNotCodexWhenTheAnswer %s', async (_name, body) => {
    const fetchHealth = vi.fn().mockResolvedValue(body);

    expect(await runServerCheck('other.example.com', fetchHealth, live())).toEqual({
      kind: 'failed',
      message: notCodexMessage('other.example.com'),
    });
  });

  it('ShouldReturnNullWhenTheAddressChangedWhileTheServerWasAnswering', async () => {
    const controller = new AbortController();
    const fetchHealth = vi.fn(async () => {
      controller.abort();
      return { status: 'ok', version: '1.0.0' };
    });

    expect(await runServerCheck('a.example.com', fetchHealth, controller.signal)).toBeNull();
  });

  it('ShouldReturnNullWhenTheAddressChangedAndTheRequestThenFailed', async () => {
    const controller = new AbortController();
    const fetchHealth = vi.fn(async () => {
      controller.abort();
      throw new Error('aborted');
    });

    expect(await runServerCheck('a.example.com', fetchHealth, controller.signal)).toBeNull();
  });

  it('ShouldReturnAFailureWhenTheRequestFailedAndNothingAborted', async () => {
    const fetchHealth = vi.fn().mockRejectedValue(new Error('refused'));

    expect(await runServerCheck('a.example.com', fetchHealth, live())).toEqual({
      kind: 'failed',
      message: unreachableMessage('a.example.com'),
    });
  });
});

describe('canSignIn', () => {
  const ok = { kind: 'ok', origin: 'https://codex.example.com', version: '1.0.0' } as const;

  it.each([
    ['codex.example.com', true],
    ['  https://codex.example.com/  ', true],
    ['codex.example.org', false],
    ['http://codex.example.com', false],
    ['', false],
  ])('ShouldOnlyPassForTheCheckedAddress: %s', (server, expected) => {
    expect(canSignIn(ok, server)).toBe(expected);
  });

  it.each([
    [{ kind: 'idle' }],
    [{ kind: 'checking', origin: 'https://codex.example.com' }],
    [{ kind: 'failed', message: 'x' }],
  ] as const)('ShouldNotPassUntilTheCheckHasPassed: %j', (check) => {
    expect(canSignIn(check, 'codex.example.com')).toBe(false);
  });
});

describe('checkLine', () => {
  it('ShouldShowNothingWhileIdle', () => {
    expect(checkLine({ kind: 'idle' })).toBeNull();
  });

  it('ShouldShowCheckingTheHost', () => {
    expect(checkLine({ kind: 'checking', origin: 'https://codex.example.com:8443' })).toEqual({
      tone: 'muted',
      text: 'Checking codex.example.com:8443…',
    });
  });

  it('ShouldShowTheVersionWithoutBuildMetadataAsConnected', () => {
    expect(
      checkLine({ kind: 'ok', origin: 'https://codex.example.com', version: '1.0.0+abc123' }),
    ).toEqual({ tone: 'ok', text: 'Codex 1.0.0 · connected' });
  });

  it('ShouldShowAFailureInDanger', () => {
    expect(checkLine({ kind: 'failed', message: unreachableMessage('h') })).toEqual({
      tone: 'danger',
      text: "Can't reach h. Check the address and that the server is running.",
    });
  });
});
