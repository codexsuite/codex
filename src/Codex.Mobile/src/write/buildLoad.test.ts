import {
  EDITOR_BRIDGE_VERSION,
  encodeHostMessage,
  decodeHostMessage,
} from '@codex/core/editing/editorBridge';
import { describe, expect, it } from 'vitest';
import {
  kb,
  kbCategory,
  kbEntrySummary,
  manuscript,
  manuscriptScene,
  outline,
  outlineChapter,
  outlineScene,
} from '~/test-support/fakes';
import { buildEntries, buildLoad, buildScene, type WriteData } from './buildLoad';
import { chaptersOf } from './chapters';

function data(overrides: Partial<WriteData> = {}): WriteData {
  return {
    outline: outline({
      acts: [
        {
          id: 'act-1',
          title: 'The Court',
          position: 'a0',
          opener: null,
          chapters: [
            outlineChapter({
              id: 'c1',
              scenes: [
                outlineScene({
                  id: 's1',
                  number: '1.1',
                  title: null,
                  summary: null,
                  words: 120,
                  labels: [{ id: 'l1', name: 'Drafting', color: null }],
                  linkedEntries: [{ id: 'e1', name: 'Ana' }],
                }),
                outlineScene({ id: 's2', number: '1.2' }),
              ],
            }),
          ],
        },
      ],
    }),
    manuscript: manuscript({
      scenes: [manuscriptScene({ sceneId: 's1', body: 'Hello.\n', version: 4 })],
    }),
    kb: kb({
      categories: [kbCategory({ id: 'cat', name: 'Characters' })],
      entries: [
        kbEntrySummary({ id: 'e1', name: 'Ana', categoryId: 'cat', aliases: ['the queen'] }),
      ],
    }),
    beats: {
      bookId: 'book-1',
      beats: [
        {
          id: 'b1',
          sceneId: 's1',
          body: 'Tobin counts the guards.',
          targetWordCount: 400,
          isHidden: false,
          contextEntryIds: [],
          title: '',
        },
        {
          id: 'b2',
          sceneId: 's2',
          body: 'Elsewhere.',
          targetWordCount: null,
          isHidden: false,
          contextEntryIds: [],
          title: 'Other',
        },
      ],
    },
    additions: {
      bookId: 'book-1',
      additions: [{ id: 'a1', sceneId: 's1', entryId: 'e1', body: 'Ana now wears grey.' }],
    },
    ...overrides,
  };
}

describe('buildScene', () => {
  const book = data();
  const [s1, s2] = chaptersOf(book.outline)[0]!.scenes;

  it('ShouldCarryTheCardTheBeatsTheAdditionsAndTheProse', () => {
    expect(buildScene(s1!, book)).toEqual({
      id: 's1',
      number: '1.1',
      title: '',
      labels: ['Drafting'],
      words: 120,
      summary: '',
      chips: [{ id: 'e1', name: 'Ana', category: 'Characters' }],
      beats: [
        {
          id: 'b1',
          title: '',
          body: 'Tobin counts the guards.',
          targetWordCount: 400,
          hidden: false,
        },
      ],
      additions: [{ id: 'a1', entryId: 'e1', entryName: 'Ana', body: 'Ana now wears grey.' }],
      prose: 'Hello.\n',
      version: 4,
    });
  });

  it('WhenTheSceneHasNoProseRowYet_ShouldBeEmptyAtVersionZero', () => {
    expect(buildScene(s2!, book)).toMatchObject({ prose: '', version: 0, additions: [] });
    expect(buildScene(s2!, book).beats.map((b) => b.id)).toEqual(['b2']);
  });

  it('WhenAChipsEntryIsNotInTheKb_ShouldLeaveItsCategoryEmpty', () => {
    const lonely = data({ kb: kb({ categories: [], entries: [] }) });

    expect(buildScene(s1!, lonely).chips).toEqual([{ id: 'e1', name: 'Ana', category: '' }]);
    expect(buildScene(s1!, lonely).additions[0]?.entryName).toBe('');
  });
});

describe('buildEntries', () => {
  it('ShouldOfferEveryEntryWithItsCategoryNameAndAliases', () => {
    expect(buildEntries(data().kb)).toEqual([
      { id: 'e1', name: 'Ana', category: 'Characters', aliases: ['the queen'] },
    ]);
  });
});

describe('buildLoad', () => {
  it('ShouldBeAValidLoadForTheChaptersScenesInOrder', () => {
    const book = data();
    const chapter = chaptersOf(book.outline)[0]!;

    const load = buildLoad(chapter, book, {
      theme: 'dark',
      density: 'touch',
      readOnly: true,
      scrollToSceneId: 's2',
    });

    expect(load).toMatchObject({
      type: 'load',
      v: EDITOR_BRIDGE_VERSION,
      theme: 'dark',
      density: 'touch',
      readOnly: true,
      scrollToSceneId: 's2',
    });
    expect(load.scenes.map((s) => s.id)).toEqual(['s1', 's2']);
    expect(load.entries).toHaveLength(1);
    expect(decodeHostMessage(encodeHostMessage(load))).toEqual({ ok: true, message: load });
  });
});
