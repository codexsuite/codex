import { afterEach, describe, expect, it, vi } from 'vitest';
import quoteFlags from '../../../Codex.TestSupport/Fixtures/references/quote-flags.json';
import { stubFetch } from '../test-support/stubFetch';
import { notAQuote, otherSource, type QuoteFlag } from './quoteFlags';

// The same JSON file the C# QuoteFlagFixtureTests round-trip. The annotation makes tsc fail if a type drifts.
describe('quote flag contract', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('QuoteFlagDto list round-trips', () => {
    const typed: QuoteFlag[] = quoteFlags;
    expect(JSON.parse(JSON.stringify(typed))).toEqual(quoteFlags);
  });

  it('notAQuote posts to the flag and answers with the flags left', async () => {
    const fetchMock = stubFetch(200, quoteFlags);

    expect(await notAQuote('f1')).toEqual(quoteFlags);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/quote-flags/f1/not-a-quote');
  });

  it('otherSource sends the note', async () => {
    const fetchMock = stubFetch(200, []);

    await otherSource('f2', 'Reinertsen, 2009');

    expect(fetchMock.mock.calls[0][0]).toBe('/api/quote-flags/f2/other-source');
    expect(JSON.parse(fetchMock.mock.calls[0][1].body as string)).toEqual({
      note: 'Reinertsen, 2009',
    });
  });
});
