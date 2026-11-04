import { describe, expect, it } from 'vitest';
import type { KbTimelineNote, KbUse } from '../../api/kbEntries';
import { lateNotes } from './timelineOrder';

const use = (sceneId: string, number: string, kind: string): KbUse => ({
  sceneId,
  bookId: 'b',
  chapterNumber: 1,
  chapterTitle: 'One',
  number,
  kinds: [{ kind, count: 1 }],
  snippet: '',
});
const note = (id: string, sceneId: string): KbTimelineNote => ({
  id,
  sceneId,
  bookId: 'b',
  sceneNumber: '1.1',
  body: 'x',
});

describe('lateNotes', () => {
  it('LateNotes_WhenANoteSitsAfterTheFirstUse_ShouldNameThatUse', () => {
    const usedIn = [use('s1', '1.1', 'mention'), use('s2', '1.2', 'timeline')];
    expect([...lateNotes(usedIn, [note('n', 's2')])]).toEqual([['n', '1.1']]);
  });

  it('LateNotes_WhenTheNoteIsTheFirstUseOrBeforeIt_ShouldWarnNothing', () => {
    const usedIn = [use('s1', '1.1', 'timeline'), use('s2', '1.2', 'mention')];
    expect(lateNotes(usedIn, [note('n', 's1')]).size).toBe(0);
  });

  it('LateNotes_WhenTheEntryIsUsedOnlyByNotes_ShouldWarnNothing', () => {
    expect(lateNotes([use('s1', '1.1', 'timeline')], [note('n', 's1')]).size).toBe(0);
  });
});
