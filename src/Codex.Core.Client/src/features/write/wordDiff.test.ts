import { describe, expect, it } from 'vitest';
import { wordDiff } from './wordDiff';

describe('wordDiff', () => {
  it('wordDiff_WhenIdentical_ShouldReportNothing', () => {
    const diff = wordDiff('one two', 'one two');
    expect(diff).toEqual({ parts: [{ kind: 'same', text: 'one two' }], added: 0, removed: 0 });
  });

  it('wordDiff_WhenWordsAppended_ShouldCountThemAsAdded', () => {
    const diff = wordDiff('We shipped.', 'We shipped. We measured outcomes, not output.');
    expect(diff.added).toBe(5);
    expect(diff.removed).toBe(0);
    expect(diff.parts.map((p) => p.kind)).toEqual(['same', 'added']);
  });

  it('wordDiff_WhenAWordChanges_ShouldMarkOneRemovedAndOneAdded', () => {
    const diff = wordDiff('the quick fox', 'the slow fox');
    expect([diff.added, diff.removed]).toEqual([1, 1]);
    expect(
      diff.parts
        .filter((p) => p.kind !== 'removed')
        .map((p) => p.text)
        .join(''),
    ).toBe('the slow fox');
  });

  it('wordDiff_WhenEmptyBefore_ShouldMarkEverythingAdded', () => {
    expect(wordDiff('', 'a b c').added).toBe(3);
  });
});
