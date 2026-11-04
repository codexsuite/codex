import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetch } from '../test-support/stubFetch';
import { ApiRequestError } from './client';
import { getHealth } from './health';

afterEach(() => vi.unstubAllGlobals());

describe('getHealth', () => {
  it('Health_WhenAnOriginIsGiven_ShouldAskThatServerWithoutSessionHeaders', async () => {
    const fetchMock = stubFetch(200, { status: 'ok', version: '1.0.0+abc' });

    const health = await getHealth({ origin: 'https://codex.example.com' });

    expect(health).toEqual({ status: 'ok', version: '1.0.0+abc' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit | undefined];
    expect(url).toBe('https://codex.example.com/api/health');
    expect(init?.headers).toBeUndefined();
  });

  it('Health_WhenNoOriginIsGiven_ShouldAskThePlatformsServer', async () => {
    const fetchMock = stubFetch(200, { status: 'ok', version: '2.0.0' });

    await getHealth();

    expect((fetchMock.mock.calls[0] as [string])[0]).toBe('/api/health');
  });

  it('Health_WhenTheServerAnswersAnError_ShouldThrowWithItsStatus', async () => {
    stubFetch(503);

    const failure = await getHealth({ origin: 'https://down.example.com' }).catch(
      (e: unknown) => e,
    );

    expect(failure).toBeInstanceOf(ApiRequestError);
    expect((failure as ApiRequestError).status).toBe(503);
  });
});
