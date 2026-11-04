import type { OutlineAct, OutlineChapter, OutlineScene } from '../../api/structure';

export const wordsText = (words: number) => `${words.toLocaleString('en-US')} words`;

export const plural = (n: number, one: string, many = `${one}s`) =>
  `${n.toLocaleString('en-US')} ${n === 1 ? one : many}`;

/** Chapters the board shows: archived ones are the archive list's, not the board's. */
export const liveChapters = (act: OutlineAct): OutlineChapter[] =>
  act.chapters.filter((c) => !c.archived);

export const sceneWords = (scenes: OutlineScene[]) => scenes.reduce((sum, s) => sum + s.words, 0);

export const actScenes = (act: OutlineAct): OutlineScene[] =>
  liveChapters(act).flatMap((c) => c.scenes);

/** "Act 2: The Product Engineer", or just "Act 1" for an untitled act. */
export const actName = (index: number, title: string) =>
  title.trim() === '' ? `Act ${index + 1}` : `Act ${index + 1}: ${title}`;

/** A scene summary as one line of plain text: mentions keep their name, emphasis and code marks go. */
export function plainSummary(markdown: string | null): string {
  if (markdown === null) return '';
  return markdown
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[*_`]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/** 1,840 words as `1.8k`, 420 as `420` (the mockup's `kw`). */
export const compactWords = (words: number) =>
  words >= 1000 ? `${(words / 1000).toFixed(1).replace(/\.0$/, '')}k` : String(words);
