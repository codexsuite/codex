import { describe, expect, it } from 'vitest';
import type { LayoutTemplate } from '@codex/core/api/layout';
import type { Outline, OutlineChapter } from '@codex/core/api/structure';
import { estimatePages, layoutLabel } from './pageEstimate';

function template(slug: string, name: string, columns = 1): LayoutTemplate {
  return {
    slug,
    name,
    version: '1.0.0',
    engine: 'tectonic',
    trims: [],
    columns,
    hasPreviewCss: false,
    options: [],
    notices: [],
  };
}

function chapter(...words: number[]): OutlineChapter {
  return {
    id: `c${words.join('-')}`,
    number: 1,
    title: 'Chapter',
    slug: 'chapter',
    position: 'a',
    archived: false,
    archivedSceneCount: 0,
    scenes: words.map((w, i) => ({
      id: `s${i}`,
      number: String(i + 1),
      title: null,
      summary: null,
      status: 'draft',
      position: 'a',
      words: w,
      hasPendingDraft: false,
      labels: [],
      linkedEntries: [],
    })),
    opener: null,
  };
}

const outline: Outline = {
  bookId: 'b',
  acts: [
    {
      id: 'a1',
      title: '',
      position: 'a',
      chapters: [chapter(200, 100), chapter(126)],
      opener: null,
    },
    { id: 'a2', title: '', position: 'b', chapters: [chapter(0)], opener: null },
  ],
};

const novel = template('novel-6x9', 'Novel (6 x 9)');

describe('estimatePages', () => {
  it('starts each chapter on a new page after four front-matter pages', () => {
    // 6×9 novel: 125 words a page, so 300 → 3, 126 → 2, 0 → 0.
    expect(estimatePages(outline, novel, '6x9')).toBe(4 + 3 + 2 + 0);
  });

  it('fits fewer words on a smaller novel trim', () => {
    // 104 words a page: 300 → 3, 126 → 2.
    expect(estimatePages({ bookId: 'b', acts: [outline.acts[0]] }, novel, '5.5x8.5')).toBe(9);
    expect(
      estimatePages(
        { bookId: 'b', acts: [{ ...outline.acts[0], chapters: [chapter(209)] }] },
        novel,
        '5.5x8.5',
      ),
    ).toBe(4 + 3);
  });

  it('uses the manuscript and two-column rates', () => {
    const one = { bookId: 'b', acts: [{ ...outline.acts[0], chapters: [chapter(236)] }] };
    expect(estimatePages(one, template('manuscript', 'Manuscript'), 'us-letter')).toBe(4 + 2);
    expect(
      estimatePages(one, template('fantasy-rpg', 'Fantasy RPG (5e style)', 2), 'us-letter'),
    ).toBe(4 + 2);
  });
});

describe('layoutLabel', () => {
  it('drops the parenthetical and names the trim', () => {
    expect(layoutLabel(novel, '6x9')).toBe('Novel 6×9');
    expect(layoutLabel(template('fantasy-rpg', 'Fantasy RPG (5e style)', 2), 'us-letter')).toBe(
      'Fantasy RPG Letter',
    );
    expect(layoutLabel(template('manuscript', 'Manuscript'), 'a4')).toBe('Manuscript A4');
  });
});
