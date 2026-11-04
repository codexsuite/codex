import { describe, expect, it } from 'vitest';
import {
  outline,
  outlineAct,
  outlineChapter,
  outlineScene,
  kb,
  kbCategory,
  kbEntrySummary,
} from '~/test-support/fakes';
import {
  additionCounts,
  buildPlanSections,
  entryCategories,
  findScene,
  rowKey,
  sceneHeading,
  sceneNumberInChapter,
} from './planModel';

const open = () => false;

const book = outline({
  acts: [
    outlineAct({
      id: 'a1',
      title: 'The Court',
      chapters: [
        outlineChapter({
          id: 'c1',
          number: 1,
          scenes: [
            outlineScene({ id: 's1', number: '1.1', words: 100 }),
            outlineScene({ id: 's2', number: '1.2', words: 50 }),
          ],
        }),
        outlineChapter({ id: 'c-old', archived: true, scenes: [outlineScene({ id: 'gone' })] }),
        outlineChapter({
          id: 'c2',
          number: 2,
          scenes: [outlineScene({ id: 's3', number: '2.1', words: 25 })],
        }),
      ],
    }),
    outlineAct({
      id: 'a2',
      title: '',
      chapters: [outlineChapter({ id: 'c3', number: 3, scenes: [outlineScene({ id: 's4' })] })],
    }),
  ],
});

describe('buildPlanSections', () => {
  it('ListsEachActWithItsChaptersAndScenesInReadingOrder', () => {
    const sections = buildPlanSections(book, { kind: 'book' }, open);

    expect(sections.map((s) => [s.act.id, s.index, s.chapters, s.words])).toEqual([
      ['a1', 0, 2, 175],
      ['a2', 1, 1, 3000],
    ]);
    expect(sections[0]?.data.map(rowKey)).toEqual([
      'chapter-c1',
      'scene-s1',
      'scene-s2',
      'chapter-c2',
      'scene-s3',
    ]);
  });

  it('LeavesOutArchivedChaptersAndTheirScenes', () => {
    const rows = buildPlanSections(book, { kind: 'book' }, open).flatMap((s) => s.data.map(rowKey));

    expect(rows).not.toContain('chapter-c-old');
    expect(rows).not.toContain('scene-gone');
  });

  it('GivesAChapterItsOwnSceneAndWordCounts', () => {
    const first = buildPlanSections(book, { kind: 'book' }, open)[0]?.data[0];

    expect(first).toMatchObject({ kind: 'chapter', scenes: 2, words: 150 });
  });

  it('EmptiesACollapsedActButKeepsItsHeaderAndCounts', () => {
    const sections = buildPlanSections(book, { kind: 'book' }, (id) => id === 'a1');

    expect(sections[0]).toMatchObject({ collapsed: true, data: [], chapters: 2, words: 175 });
    expect(sections[1]?.collapsed).toBe(false);
  });

  it('NarrowsToOneActAndKeepsItsPositionNumber', () => {
    const sections = buildPlanSections(book, { kind: 'act', id: 'a2' }, open);

    expect(sections.map((s) => [s.act.id, s.index])).toEqual([['a2', 1]]);
  });

  it('NarrowsToOneChapter', () => {
    const sections = buildPlanSections(book, { kind: 'chapter', id: 'c2' }, open);

    expect(sections).toHaveLength(1);
    expect(sections[0]?.data.map(rowKey)).toEqual(['chapter-c2', 'scene-s3']);
  });

  it('ShowsAnEmptyRowForAnActWithNoLiveChapters', () => {
    const empty = outline({ acts: [outlineAct({ chapters: [] })] });

    expect(buildPlanSections(empty, { kind: 'book' }, open)[0]?.data).toEqual([{ kind: 'empty' }]);
  });
});

describe('scene numbers', () => {
  it.each([
    ['3.2', '2', 'Chapter 3 · Scene 2'],
    ['12.10', '10', 'Chapter 12 · Scene 10'],
    ['4', '4', 'Scene 4'],
  ])('Number_%s_ShouldReadAsSceneNumber%sAndHeading%s', (number, scene, heading) => {
    expect(sceneNumberInChapter(number)).toBe(scene);
    expect(sceneHeading(number)).toBe(heading);
  });
});

describe('findScene', () => {
  it('FindsASceneInAnyLiveChapter', () => {
    expect(findScene(book, 's4')?.id).toBe('s4');
  });

  it('DoesNotFindASceneOfAnArchivedChapterOrAnUnknownId', () => {
    expect(findScene(book, 'gone')).toBeUndefined();
    expect(findScene(book, 'nope')).toBeUndefined();
  });
});

describe('entryCategories', () => {
  it('MapsEachEntryToItsCategoryTagWhenThemeHasAColourForIt', () => {
    const map = entryCategories(
      kb({
        categories: [
          kbCategory({ id: 'c-person', tag: 'person' }),
          kbCategory({ id: 'c-odd', tag: 'weapon' }),
          kbCategory({ id: 'c-none', tag: null }),
        ],
        entries: [
          kbEntrySummary({ id: 'e1', categoryId: 'c-person' }),
          kbEntrySummary({ id: 'e2', categoryId: 'c-odd' }),
          kbEntrySummary({ id: 'e3', categoryId: 'c-none' }),
        ],
      }),
    );

    expect([...map]).toEqual([['e1', 'person']]);
  });
});

describe('additionCounts', () => {
  it('CountsAdditionsPerScene', () => {
    const counts = additionCounts([{ sceneId: 's1' }, { sceneId: 's2' }, { sceneId: 's1' }]);

    expect([...counts]).toEqual([
      ['s1', 2],
      ['s2', 1],
    ]);
  });
});
