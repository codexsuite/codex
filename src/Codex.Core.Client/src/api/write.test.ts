import { describe, expect, it } from 'vitest';
import manuscript from '../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json';
import kbAdditions from '../../../Codex.TestSupport/Fixtures/manuscript/kb-additions.json';
import type { BookKbAdditions, Manuscript } from './write';

// The same JSON file the C# ManuscriptDtoFixtureTests round-trip.
describe('write contract fixtures', () => {
  it('ManuscriptDto round-trips', () => {
    const typed = manuscript satisfies Manuscript;
    expect(JSON.parse(JSON.stringify(typed))).toEqual(manuscript);
  });

  it('BookKbAdditionsDto round-trips', () => {
    const typed = kbAdditions satisfies BookKbAdditions;
    expect(JSON.parse(JSON.stringify(typed))).toEqual(kbAdditions);
  });
});
