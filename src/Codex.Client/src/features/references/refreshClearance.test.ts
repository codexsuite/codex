import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { refreshClearance } from './refreshClearance';

describe('refreshClearance', () => {
  it('marks the book bar badge, the strip, the lists and the markers stale for that book only', async () => {
    const client = new QueryClient();
    const keys = ['book', 'clearance-summary', 'sources', 'sources-csl', 'markers'];
    for (const key of keys) {
      client.setQueryData([key, 'b1'], 1);
      client.setQueryData([key, 'b2'], 1);
    }

    await refreshClearance(client, 'b1');

    expect(keys.map((key) => client.getQueryState([key, 'b1'])?.isInvalidated)).toEqual(
      keys.map(() => true),
    );
    expect(keys.map((key) => client.getQueryState([key, 'b2'])?.isInvalidated)).toEqual(
      keys.map(() => false),
    );
  });
});
