import { Fragment } from 'react';
import type { Outline } from '@codex/core/api/structure';
import { emphasisOf } from '@codex/core/features/plan/emphasis';
import { liveChapters } from '@codex/core/features/plan/format';
import type { PlanFilter } from '@codex/core/features/plan/planFilter';
import { actInScope, chapterInScope, type PlanScope } from '@codex/core/features/plan/planScope';
import { OutlineActRow } from './OutlineActRow';
import { OutlineChapterRow } from './OutlineChapterRow';
import { OutlineSceneRow } from './OutlineSceneRow';
import { planTestIds } from './testIds';

// The board's order as a dense table: a group row per act, a row per chapter, a row per scene. Like the board, it
// draws only what is in the book bar's scope.
export function OutlineView({
  outline,
  highlightedEntry,
  filter,
  scope,
}: {
  outline: Outline;
  highlightedEntry: string | null;
  filter: PlanFilter;
  scope: PlanScope;
}) {
  return (
    <table data-testid={planTestIds.outline} className="w-full table-fixed border-collapse text-sm">
      <thead>
        <tr className="text-left text-2xs tracking-[.02em] text-muted uppercase">
          <th className="w-14 px-2 py-1 text-right">#</th>
          <th className="px-2 py-1">Scene summary</th>
          <th className="w-56 px-2 py-1">Codex</th>
          <th className="w-44 px-2 py-1">Labels</th>
          <th className="w-20 px-2 py-1 text-right">Words</th>
        </tr>
      </thead>
      <tbody>
        {outline.acts.map((act, index) =>
          !actInScope(act, scope) ? null : (
            <Fragment key={act.id}>
              <OutlineActRow act={act} index={index} />
              {liveChapters(act)
                .filter((chapter) => chapterInScope(chapter, scope))
                .map((chapter) => (
                  <Fragment key={chapter.id}>
                    <OutlineChapterRow chapter={chapter} />
                    {chapter.scenes.map((scene) => (
                      <OutlineSceneRow
                        key={scene.id}
                        scene={scene}
                        emphasis={emphasisOf(scene, highlightedEntry, filter)}
                      />
                    ))}
                  </Fragment>
                ))}
            </Fragment>
          ),
        )}
      </tbody>
    </table>
  );
}
