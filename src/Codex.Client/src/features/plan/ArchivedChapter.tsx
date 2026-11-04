import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { archivedScenesKey, planApi } from '@codex/core/api/plan';
import type { OutlineChapter } from '@codex/core/api/structure';
import { plural } from '@codex/core/features/plan/format';
import { ArchivedSceneRow } from './ArchivedSceneRow';
import { planTestIds } from './testIds';
import { useChapterOps } from './useChapterOps';
import { useCanWrite } from '../../shell/bookAccess';

// One chapter's entry in the Archived panel: an archived chapter gets its title (muted, with Restore) and the scenes it
// still holds; a live chapter just lists the scenes taken off it. Scenes are fetched when the panel opens.
export function ArchivedChapter({
  chapter,
  focused,
}: {
  chapter: OutlineChapter;
  focused: boolean;
}) {
  const { bookId = '' } = useParams();
  const { restore } = useChapterOps();
  const canWrite = useCanWrite();
  const archived = useQuery({
    queryKey: archivedScenesKey(bookId, chapter.id),
    queryFn: () => planApi.archivedScenes(chapter.id),
    enabled: chapter.archivedSceneCount > 0,
  });
  return (
    <div
      id={`archived-chapter-${chapter.id}`}
      data-testid={planTestIds.archivedChapter(chapter.id)}
      data-focused={focused}
      className={`flex flex-col gap-1 rounded-sm ${focused ? 'bg-accent-soft' : ''}`}
    >
      {chapter.archived && (
        <div className="flex items-center justify-between gap-2 px-2 pt-1.5">
          <div className="flex min-w-0 flex-col">
            <span className="text-2xs tracking-[.02em] text-muted uppercase">
              Archived chapter · {plural(chapter.scenes.length, 'scene')}
            </span>
            <span className="truncate font-semibold text-text">{chapter.title}</span>
          </div>
          {canWrite && (
            <button
              type="button"
              data-testid={planTestIds.restoreChapter(chapter.id)}
              onClick={() => void restore(chapter.id)}
              className="h-[22px] shrink-0 cursor-pointer rounded-xs border border-border bg-transparent px-1.5 text-xs font-medium text-text hover:bg-surface-2"
            >
              Restore
            </button>
          )}
        </div>
      )}
      {archived.isError && (
        <span role="alert" className="text-xs text-danger">
          {archived.error.message}
        </span>
      )}
      {archived.data?.map((scene) => (
        <ArchivedSceneRow key={scene.id} scene={scene} chapter={chapter} />
      ))}
    </div>
  );
}
