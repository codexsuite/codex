import { useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { SaveStatus } from '../../editing/SaveStatus';
import { SlotFill } from '../../shell/Slots';
import { useOutline } from '../../api/outlineQueries';
import { ErrorState } from '../../shell/ErrorState';
import { useHighlight } from '../../shell/useHighlight';
import type { DeepLink } from '@codex/core/shell/deepLink';
import { useDeepLink } from '../../shell/useDeepLink';
import { ToolbarSelect } from '../library/ToolbarSelect';
import { countsLabel } from '@codex/core/features/write/scope';
import { Board } from './Board';
import { ImportBanner } from './ImportBanner';
import { OutlineView } from './OutlineView';
import { PlanEditProvider } from './PlanEditProvider';
import { PlanToolbar } from './PlanToolbar';
import { noFilter, type PlanFilter } from '@codex/core/features/plan/planFilter';
import { useCollapsedActs, usePlanView } from './planPrefs';
import {
  parseScope,
  resolveScope,
  scopeOptions,
  scopeValue,
  scopeWords,
  wholeBook,
  type PlanScope,
} from '@codex/core/features/plan/planScope';
import { planTestIds } from './testIds';
import { useCreate } from './useCreate';
import { usePlanTarget } from './usePlanTarget';
import './plan.css';

// The Plan screen: the book's structure from one outline query, as a board of act sections or as a table.
export function PlanPage() {
  const { bookId = '' } = useParams();
  return (
    <PlanEditProvider bookId={bookId}>
      <PlanScreen bookId={bookId} />
    </PlanEditProvider>
  );
}

// `#archived=<chapter>`: Write's link to an archived chapter lands on the Archived list at that chapter (rule R1).
// The shell's link parser ignores keys it does not own, so the hash is read here.
const archivedChapterOf = (hash: string) =>
  new URLSearchParams(hash.replace(/^#/, '')).get('archived') ?? undefined;

function PlanScreen({ bookId }: { bookId: string }) {
  const outline = useOutline(bookId);
  const deepLink = useDeepLink();
  const archivedChapterId = archivedChapterOf(useLocation().hash);
  const link: DeepLink = deepLink.ok ? deepLink.link : { screen: 'plan' };
  const [highlightedEntry] = useHighlight(bookId, link.kb);
  const [view, setView] = usePlanView(bookId);
  const { isCollapsed, setCollapsed, setAllCollapsed } = useCollapsedActs(bookId);
  const [filter, setFilter] = useState<PlanFilter>(noFilter);
  const [pickedScope, setScope] = useState<PlanScope>(wholeBook);
  const scope = resolveScope(outline.data, pickedScope);
  const { createAct } = useCreate();
  const acts = outline.data?.acts ?? [];
  const allCollapsed = acts.length > 0 && acts.every((act, index) => isCollapsed(act.id, index));
  // A new act goes at the end of the book: show the whole book, and open the act even where acts start collapsed.
  const addAct = async () => {
    setScope(wholeBook);
    const act = await createAct();
    if (act) setCollapsed(act.id, false);
  };
  usePlanTarget({
    outline: outline.data,
    scene: link.scene,
    chapter: link.chapter,
    view,
    setView,
    isCollapsed,
    setCollapsed,
  });

  return (
    <div data-testid={planTestIds.page} className="flex min-h-full flex-col">
      <SlotFill slot="saveStatus">
        <SaveStatus />
      </SlotFill>
      {outline.data && (
        <SlotFill slot="scope">
          <div className="ml-2 flex min-w-0 items-center gap-2">
            <ToolbarSelect
              label="Scope"
              testId={planTestIds.scope}
              value={scopeValue(scope)}
              options={scopeOptions(outline.data)}
              onChange={(value) => setScope(parseScope(value))}
            />
            <span
              data-testid={planTestIds.stats}
              className="text-xs whitespace-nowrap text-muted tabular-nums"
            >
              {countsLabel({ words: scopeWords(outline.data, scope) })}
            </span>
          </div>
        </SlotFill>
      )}
      <PlanToolbar
        view={view}
        onChange={setView}
        outline={outline.data}
        filter={filter}
        onFilter={setFilter}
        allCollapsed={allCollapsed}
        onToggleAll={() =>
          setAllCollapsed(
            acts.map((act) => act.id),
            !allCollapsed,
          )
        }
        onNewAct={() => void addAct()}
        archivedChapterId={archivedChapterId}
      />
      <div className="px-3 pt-2 pb-5">
        {outline.isError && (
          <ErrorState
            testId={planTestIds.loadError}
            title="The plan could not be loaded"
            detail={outline.error.message}
          />
        )}
        {outline.data && (
          <>
            <ImportBanner />
            {view === 'board' ? (
              <Board
                outline={outline.data}
                isCollapsed={isCollapsed}
                onToggleAct={setCollapsed}
                highlightedEntry={highlightedEntry}
                filter={filter}
                scope={scope}
                onAddAct={() => void addAct()}
              />
            ) : (
              <OutlineView
                outline={outline.data}
                highlightedEntry={highlightedEntry}
                filter={filter}
                scope={scope}
              />
            )}
          </>
        )}
      </div>
    </div>
  );
}
