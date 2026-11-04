import { describe, expect, it } from 'vitest';
import addResult from '../../../Codex.TestSupport/Fixtures/references/add-result.json';
import importRequest from '../../../Codex.TestSupport/Fixtures/references/import-request.json';
import importResult from '../../../Codex.TestSupport/Fixtures/references/import-result.json';
import lookupResult from '../../../Codex.TestSupport/Fixtures/references/lookup-result.json';
import type { AddSourceResult, ImportItem, ImportResult, LookupResult } from './referenceLookup';

// The same JSON files the C# SourceDtoFixtureTests round-trip. The annotation makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('reference lookup contract fixtures', () => {
  it('LookupSourceResultDto round-trips', () => {
    const typed: LookupResult = lookupResult as LookupResult;
    expect(roundTrip(typed)).toEqual(lookupResult);
  });

  it('AddSourceResult round-trips', () => {
    const typed: AddSourceResult = addResult;
    expect(roundTrip(typed)).toEqual(addResult);
  });

  it('ImportSourcesRequest round-trips', () => {
    const typed: { items: ImportItem[] } = importRequest as { items: ImportItem[] };
    expect(roundTrip(typed)).toEqual(importRequest);
  });

  it('ImportSourcesResult round-trips', () => {
    const typed: ImportResult = importResult;
    expect(roundTrip(typed)).toEqual(importResult);
  });
});
