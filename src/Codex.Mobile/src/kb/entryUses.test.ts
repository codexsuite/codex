import { describe, expect, it } from 'vitest';
import type { KbUse } from '@codex/core/api/kbEntries';
import { acceptableAlias, additionsFor, useLabel, usesInBook } from './entryUses';

const use = (extra: Partial<KbUse> = {}): KbUse => ({
  sceneId: 's1',
  bookId: 'book-1',
  chapterNumber: 2,
  chapterTitle: 'The Gallery',
  number: '2.1',
  kinds: [],
  snippet: '',
  ...extra,
});

describe('useLabel', () => {
  it('UseLabel_ForANumberedChapter_ShouldSayChapterAndScene', () => {
    expect(useLabel(use())).toBe('Chapter 2 · Scene 1');
  });

  it('UseLabel_ForAnUnnumberedChapter_ShouldUseItsTitle', () => {
    expect(useLabel(use({ chapterNumber: null, chapterTitle: 'Prologue', number: '3' }))).toBe(
      'Prologue · Scene 3',
    );
  });

  it('UseLabel_ForAnUnnumberedUntitledChapter_ShouldSayChapter', () => {
    expect(useLabel(use({ chapterNumber: null, chapterTitle: ' ', number: '1' }))).toBe(
      'Chapter · Scene 1',
    );
  });
});

describe('usesInBook', () => {
  it('UsesInBook_ShouldDropScenesOfOtherBooks', () => {
    const uses = [use(), use({ sceneId: 's2', bookId: 'book-2' })];

    expect(usesInBook(uses, 'book-1').map((u) => u.sceneId)).toEqual(['s1']);
  });
});

describe('additionsFor', () => {
  const additions = [
    { id: 'a1', sceneId: 's1', entryId: 'e1', body: 'Lies with a level voice.' },
    { id: 'a2', sceneId: 's9', entryId: 'e1', body: 'Unplaced.' },
    { id: 'a3', sceneId: 's1', entryId: 'other', body: 'Not ours.' },
  ];

  it('AdditionsFor_ShouldKeepTheEntrysAndLabelThemByScene', () => {
    expect(additionsFor(additions, 'e1', [use()])).toEqual([
      { id: 'a1', body: 'Lies with a level voice.', where: 'Chapter 2 · Scene 1' },
      { id: 'a2', body: 'Unplaced.', where: null },
    ]);
  });
});

describe('acceptableAlias', () => {
  it('AcceptableAlias_ShouldTrim', () => {
    expect(acceptableAlias('  The Queen ', [], 'Maren')).toBe('The Queen');
  });

  it.each([
    ['', 'blank'],
    ['  ', 'spaces'],
    ['maren', 'the name'],
    ['the queen', 'an existing alias'],
  ])('AcceptableAlias_When%sIsTyped_ShouldRefuse (%s)', (typed) => {
    expect(acceptableAlias(typed, ['The Queen'], 'Maren')).toBeNull();
  });
});
