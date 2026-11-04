import { describe, expect, it } from 'vitest';
import type { Outline, OutlineChapter, OutlineScene } from '../../api/structure';
import type { Manuscript } from '../../api/write';
import { buildManuscriptView, chapterLinkLabels, resolveTarget } from './manuscriptView';

const scene = (id: string, number: string, words: number): OutlineScene => ({
  id,
  number,
  title: null,
  summary: null,
  status: 'draft',
  position: 'a0',
  words,
  hasPendingDraft: false,
  labels: [],
  linkedEntries: [],
});

const chapter = (
  id: string,
  number: number | null,
  scenes: OutlineScene[],
  archived = false,
): OutlineChapter => ({
  id,
  number,
  title: `Title ${id}`,
  slug: `slug-${id}`,
  position: 'a0',
  archived,
  archivedSceneCount: 0,
  scenes,
  opener: null,
});

const outline: Outline = {
  bookId: 'b',
  acts: [
    {
      id: 'a1',
      title: 'First',
      position: 'a0',
      chapters: [chapter('c1', 1, [scene('s1', '1.1', 1200), scene('s2', '1.2', 0)])],
      opener: null,
    },
    { id: 'a2', title: 'Empty', position: 'a1', chapters: [], opener: null },
    {
      id: 'a3',
      title: 'Third',
      position: 'a2',
      chapters: [chapter('c2', 2, [scene('s3', '2.1', 5)]), chapter('c3', null, [], true)],
      opener: null,
    },
  ],
};
const manuscript: Manuscript = {
  bookId: 'b',
  scenes: [{ sceneId: 's1', body: 'One.', version: 1 }],
};

describe('buildManuscriptView', () => {
  const acts = buildManuscriptView(outline, manuscript);

  it('BuildManuscriptView_WhenActHasNoLiveChapter_ShouldDrawNoMarker', () => {
    expect(acts.map((a) => a.marker)).toEqual(['Act 1 · First', 'Act 3 · Third']);
    expect(acts.map((a) => a.number)).toEqual([1, 3]);
  });

  it('BuildManuscriptView_WhenActHasNoTitle_ShouldDropTheSeparator', () => {
    const untitled = { ...outline, acts: [{ ...outline.acts[0], title: ' ' }] };
    expect(buildManuscriptView(untitled, manuscript)[0].marker).toBe('Act 1');
  });

  it('BuildManuscriptView_ShouldLabelScenesAndCounts', () => {
    const first = acts[0].chapters[0];
    expect(first.overline).toBe('Chapter 1');
    expect(first.reference).toBe('ch:slug-c1');
    expect(first.countsLabel).toBe('2 scenes · 1,200 words');
    expect(first.scenes.map((s) => [s.heading, s.wordsLabel, s.body])).toEqual([
      ['Chapter 1 · Scene 1', '1,200 words', 'One.'],
      ['Chapter 1 · Scene 2', 'Not written', ''],
    ]);
  });

  it('BuildManuscriptView_WhenChapterArchived_ShouldLeaveItOut', () => {
    expect(acts[1].chapters.map((c) => c.chapter.id)).toEqual(['c2']);
  });
});

describe('chapterLinkLabels', () => {
  it('ChapterLinkLabels_ShouldUseLiveNumbersAndShowAnArchivedChapterByTitle', () => {
    const labels = chapterLinkLabels(outline);
    expect(labels.get('c2')).toMatchObject({ text: '§ Chapter 2', missing: false });
    expect(labels.get('c3')).toMatchObject({ missing: false, archived: true });
    expect(labels.get('c3')?.text).toBe(labels.get('c3')?.title);
    expect(labels.get('gone')).toBeUndefined();
  });
});

describe('resolveTarget', () => {
  const acts = buildManuscriptView(outline, manuscript);

  it('ResolveTarget_WhenSceneNumberKnown_ShouldFindIt', () => {
    expect(resolveTarget(acts, { scene: '2.1' })).toEqual({ kind: 'scene', id: 's3' });
    expect(resolveTarget(acts, { chapter: '2' })).toEqual({ kind: 'chapter', id: 'c2' });
  });

  it('ResolveTarget_WhenNothingMatches_ShouldBeNull', () => {
    expect(resolveTarget(acts, { scene: '9.9' })).toBeNull();
    expect(resolveTarget(acts, { chapter: '7' })).toBeNull();
    expect(resolveTarget(acts, {})).toBeNull();
  });

  it('ResolveTarget_WhenChapterIsUnnumbered_ShouldNotMatchTheTextNull', () => {
    const preface: Outline = {
      bookId: 'b',
      acts: [
        {
          id: 'a0',
          title: 'Front',
          position: 'a0',
          chapters: [chapter('c0', null, [])],
          opener: null,
        },
      ],
    };
    expect(resolveTarget(buildManuscriptView(preface, manuscript), { chapter: 'null' })).toBeNull();
  });
});
