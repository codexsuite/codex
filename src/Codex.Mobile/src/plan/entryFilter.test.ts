import { describe, expect, it } from 'vitest';
import { kbEntrySummary } from '~/test-support/fakes';
import { offeredEntries } from './entryFilter';

const entries = [
  kbEntrySummary({ id: 'e1', name: 'Tobin', aliases: ['The Counter'] }),
  kbEntrySummary({ id: 'e2', name: 'Ana', aliases: [] }),
  kbEntrySummary({ id: 'e3', name: 'Hollow Court', aliases: ['The Court'] }),
];

describe('offeredEntries', () => {
  it('OffersEveryEntryByNameWhenNothingIsTyped', () => {
    expect(offeredEntries(entries, [], '').map((e) => e.id)).toEqual(['e2', 'e3', 'e1']);
  });

  it('LeavesOutEntriesAlreadyLinked', () => {
    expect(offeredEntries(entries, ['e2'], '').map((e) => e.id)).toEqual(['e3', 'e1']);
  });

  it('MatchesNameOrAliasIgnoringCase', () => {
    expect(offeredEntries(entries, [], 'court').map((e) => e.id)).toEqual(['e3']);
    expect(offeredEntries(entries, [], ' counter ').map((e) => e.id)).toEqual(['e1']);
  });

  it('OffersNothingWhenNothingMatches', () => {
    expect(offeredEntries(entries, [], 'zzz')).toEqual([]);
  });
});
