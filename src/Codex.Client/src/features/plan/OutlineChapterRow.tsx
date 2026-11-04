import type { OutlineChapter } from '@codex/core/api/structure';
import { sceneWords } from '@codex/core/features/plan/format';
import { planTestIds } from './testIds';

export function OutlineChapterRow({ chapter }: { chapter: OutlineChapter }) {
  const words = sceneWords(chapter.scenes);
  return (
    <tr data-testid={planTestIds.outlineChapter(chapter.id)} className="font-semibold">
      <td className="px-2 py-1 text-right text-muted">{chapter.number ?? ''}</td>
      <td colSpan={3} className="px-2 py-1">
        {chapter.title}
      </td>
      <td className="px-2 py-1 text-right">{words > 0 ? words.toLocaleString('en-US') : '—'}</td>
    </tr>
  );
}
