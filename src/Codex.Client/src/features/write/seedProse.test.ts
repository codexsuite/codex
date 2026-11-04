import { describe, expect, it } from 'vitest';
import seedProse from '../../../../Codex.TestSupport/Fixtures/manuscript/seed-prose.json';
import { parse, serialize } from '../../prose';

// seed-prose.json holds every live prose body of the seed (the C# SeedProseFixtureTests pin it to the seed;
// the regeneration command is in that class). Each body must survive the editor's parser untouched.
describe('seed prose', () => {
  const bodies = Object.entries(seedProse);

  it('SeedProse_ShouldHoldTheSeedsNinetyTwoBodies', () => {
    expect(bodies).toHaveLength(92);
  });

  it.each(bodies)('SeedProse_WhenParsedAndSerialized_ShouldRoundTrip %s', (_sceneId, body) => {
    expect(serialize(parse(body, 'prose'))).toBe(body);
  });
});
