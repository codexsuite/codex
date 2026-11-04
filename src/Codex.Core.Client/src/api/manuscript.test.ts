import { describe, expect, it } from 'vitest';
import prose from '../../../Codex.TestSupport/Fixtures/manuscript/prose.json';
import saveProseRequest from '../../../Codex.TestSupport/Fixtures/manuscript/save-prose-request.json';
import saveProseResult from '../../../Codex.TestSupport/Fixtures/manuscript/save-prose-result.json';
import type { Prose, SaveProseRequest, SaveProseResult } from './manuscript';

// The same JSON files the C# StructureDtoFixtureTests round-trip.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('manuscript contract fixtures', () => {
  it('ProseDto round-trips', () => {
    const typed = prose satisfies Prose;
    expect(roundTrip(typed)).toEqual(prose);
  });

  it('save request and result round-trip', () => {
    const request = saveProseRequest satisfies SaveProseRequest;
    const result = saveProseResult satisfies SaveProseResult;
    expect(roundTrip(request)).toEqual(saveProseRequest);
    expect(roundTrip(result)).toEqual(saveProseResult);
  });
});
