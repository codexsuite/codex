import { parse } from '../../prose';
import { nodes } from './blockOps';
import { countWords } from '@codex/core/features/write/draftText';

/** The words of the prose under a beat: every block after its line, up to the next beat line or the end of the scene. */
export function beatProseWords(body: string, beatId: string): number {
  const blocks = nodes(parse(body, 'prose'));
  const isBeat = (n: (typeof blocks)[number]) =>
    n.type.name === 'directive' && n.attrs.name === 'beat';
  const start = blocks.findIndex((b) => isBeat(b) && b.attrs.ref === beatId);
  if (start < 0) return 0;
  let words = 0;
  for (const block of blocks.slice(start + 1)) {
    if (isBeat(block)) break;
    words += countWords(block.textContent);
  }
  return words;
}
