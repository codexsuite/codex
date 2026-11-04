import { describe, expect, it } from 'vitest';
import prompts from '../../../Codex.TestSupport/Fixtures/prompts.json';
import type { Prompts } from './prompts';

// The same JSON the C# PromptsDtoFixtureTests round-trip; `satisfies` makes tsc fail if the type drifts.
describe('prompts contract fixture', () => {
  it('PromptsDto round-trips and has one default model', () => {
    const typed = prompts satisfies Prompts;
    expect(JSON.parse(JSON.stringify(typed))).toEqual(prompts);
    expect(typed.models.filter((m) => m.default)).toHaveLength(1);
  });
});
