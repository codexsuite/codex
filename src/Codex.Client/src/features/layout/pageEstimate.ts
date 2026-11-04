import type { LayoutTemplate } from '@codex/core/api/layout';
import type { Outline } from '@codex/core/api/structure';

// The book bar's "≈ n pages in <template> <trim>" (design/screens/print.html, wordsPerPage and chapterStarts): pages from
// word counts at the template's base size, each chapter starting on a new page after four front-matter pages
// (title, credits, contents, blank). Export gives the exact number.
const FRONT_MATTER_PAGES = 4;

const TRIM_LABELS: Record<string, string> = {
  '6x9': '6×9',
  '5.5x8.5': '5.5×8.5',
  'us-letter': 'Letter',
  a4: 'A4',
};

function wordsPerPage(template: LayoutTemplate, trim: string): number {
  if (template.slug === 'manuscript') return 235;
  if (template.slug.startsWith('novel')) return trim === '6x9' ? 125 : 104;
  return (template.columns === 2 ? 185 : 165) * (trim === 'a4' ? 1.04 : 1);
}

export function estimatePages(outline: Outline, template: LayoutTemplate, trim: string): number {
  const perPage = wordsPerPage(template, trim);
  return outline.acts
    .flatMap((act) => act.chapters)
    .reduce(
      (pages, chapter) =>
        pages + Math.ceil(chapter.scenes.reduce((words, s) => words + s.words, 0) / perPage),
      FRONT_MATTER_PAGES,
    );
}

// "Novel (6 x 9)" → "Novel": the template's name without its parenthetical, as the mockup's short name.
export function layoutLabel(template: LayoutTemplate, trim: string): string {
  return `${template.name.replace(/\s*\(.*\)\s*$/, '')} ${TRIM_LABELS[trim] ?? trim}`;
}
