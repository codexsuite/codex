import { describe, expect, it } from 'vitest';
import { outline, outlineAct, outlineChapter, outlineScene } from '~/test-support/fakes';
import { actsOf, chapterLabel, chaptersOf, neighbours, resolveTarget, sceneName } from './chapters';

const book = outline({
  acts: [
    outlineAct({
      id: 'act-1',
      title: 'The Court',
      chapters: [
        outlineChapter({
          id: 'c1',
          number: 1,
          title: 'An Empty Seat',
          scenes: [
            outlineScene({ id: 's1', number: '1.1', words: 1000 }),
            outlineScene({ id: 's2', number: '1.2', words: 500 }),
          ],
        }),
        outlineChapter({ id: 'c-old', number: null, title: 'Gone', archived: true }),
        outlineChapter({
          id: 'c2',
          number: 2,
          title: 'The Bell',
          scenes: [outlineScene({ id: 's3', number: '2.1' })],
        }),
      ],
    }),
    outlineAct({ id: 'act-empty', title: 'Nothing', chapters: [] }),
    outlineAct({
      id: 'act-2',
      title: 'Last Light',
      chapters: [
        outlineChapter({
          id: 'c3',
          number: null,
          title: 'Preface',
          scenes: [outlineScene({ id: 's4', number: '1' })],
        }),
      ],
    }),
  ],
});

describe('chapters', () => {
  it('ActsOf_ShouldKeepLiveChaptersAndDropEmptyActs', () => {
    const acts = actsOf(book);

    expect(acts.map((a) => a.id)).toEqual(['act-1', 'act-2']);
    expect(acts[0]?.chapters.map((c) => c.id)).toEqual(['c1', 'c2']);
    expect(acts[0]?.chapters[0]).toMatchObject({
      label: '1 · An Empty Seat',
      words: 1500,
      actTitle: 'The Court',
    });
  });

  it('ChapterLabel_WhenUnnumbered_ShouldBeTheTitle', () => {
    expect(chapterLabel(null, 'Preface')).toBe('Preface');
    expect(chapterLabel(3, 'Grey Ink')).toBe('3 · Grey Ink');
  });

  it('ChaptersOf_ShouldBeInReadingOrderAcrossActs', () => {
    expect(chaptersOf(book).map((c) => c.id)).toEqual(['c1', 'c2', 'c3']);
  });

  describe('resolveTarget', () => {
    const chapters = chaptersOf(book);

    it('WhenASceneIsLinked_ShouldOpenItsChapterScrolledToIt', () => {
      const target = resolveTarget(chapters, { sceneId: 's3' });

      expect(target?.chapter.id).toBe('c2');
      expect(target?.scrollToSceneId).toBe('s3');
    });

    it('WhenSceneAndChapterAreLinked_ShouldLetTheSceneWin', () => {
      expect(resolveTarget(chapters, { sceneId: 's3', chapterId: 'c1' })?.chapter.id).toBe('c2');
    });

    it('WhenAChapterIsLinked_ShouldOpenItAtTheTop', () => {
      const target = resolveTarget(chapters, { chapterId: 'c3' });

      expect(target?.chapter.id).toBe('c3');
      expect(target?.scrollToSceneId).toBeNull();
    });

    it.each([
      ['nothing', {}],
      ['an unknown scene', { sceneId: 'nope' }],
      ['an archived chapter', { chapterId: 'c-old' }],
    ])('WhenLinkedTo_%s_ShouldOpenTheFirstChapter', (_name, at) => {
      expect(resolveTarget(chapters, at)?.chapter.id).toBe('c1');
    });

    it('WhenAnUnknownSceneComesWithAChapter_ShouldFallBackToTheChapter', () => {
      expect(resolveTarget(chapters, { sceneId: 'nope', chapterId: 'c2' })?.chapter.id).toBe('c2');
    });

    it('WhenThereAreNoChapters_ShouldBeNull', () => {
      expect(resolveTarget([], { sceneId: 's1' })).toBeNull();
    });
  });

  it('Neighbours_ShouldBeNullAtTheEnds', () => {
    const chapters = chaptersOf(book);

    expect(neighbours(chapters, 'c1')).toMatchObject({ previous: null, next: { id: 'c2' } });
    expect(neighbours(chapters, 'c2')).toMatchObject({
      previous: { id: 'c1' },
      next: { id: 'c3' },
    });
    expect(neighbours(chapters, 'c3')).toMatchObject({ previous: { id: 'c2' }, next: null });
  });

  it('SceneName_ShouldUseTheNumberAfterTheLastDot', () => {
    expect(sceneName('1.2')).toBe('Scene 2');
    expect(sceneName('12.10')).toBe('Scene 10');
    expect(sceneName('3')).toBe('Scene 3');
  });
});
