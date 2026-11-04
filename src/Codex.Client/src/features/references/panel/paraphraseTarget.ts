import type { SourceDetail, SourceUse } from '@codex/core/api/referenceSources';

/** The scene address Write's links take: `ch.sc`, or the bare scene number in an unnumbered chapter. */
export const sceneOf = (use: SourceUse) =>
  use.chapterNumber === null ? String(use.sceneNumber) : `${use.chapterNumber}.${use.sceneNumber}`;

/** "Chapter n · Scene n", or "Scene n" in an unnumbered chapter. */
export const whereOf = (use: SourceUse) =>
  use.chapterNumber === null
    ? `Scene ${use.sceneNumber}`
    : `Chapter ${use.chapterNumber} · Scene ${use.sceneNumber}`;

// "Paraphrase instead" (design/README.md, Links into Write): for a source over the quote limit or quoting lyrics,
// the smallest quote that brings it back under the limit, else the largest. Null when no quote can be paraphrased.
export function paraphraseTarget(source: SourceDetail): { scene: string; where: string } | null {
  if (source.verdict.reason !== 'quote_over_limit' && source.verdict.reason !== 'lyric')
    return null;
  const quotes = source.uses
    .filter((use) => (use.kind === 'quote' || use.kind === 'lyric') && !use.pending)
    .sort((a, b) => a.words - b.words);
  const over = source.quotedWords - source.limit;
  const use = quotes.find((candidate) => candidate.words >= over) ?? quotes.at(-1);
  return use === undefined ? null : { scene: sceneOf(use), where: whereOf(use) };
}
