import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  acceptFairUse,
  acceptRedirect,
  archiveSource,
  draftPermission,
  getSource,
  getSources,
  mergeSource,
  patchSource,
  recheckSource,
  reopenSource,
  setUseKind,
  signOffSource,
  sourceKey,
  sourcesKey,
  type SourceDetail,
  type VerdictAction,
} from '@codex/core/api/referenceSources';
import { ApiRequestError } from '@codex/core/api/client';
import { usePublicationSettings } from '../../../api/settingsQueries';
import { showToast } from '@codex/core/editing/toastStore';
import { settingsLink } from '@codex/core/shell/links';
import { refsTestIds } from '../testIds';
import { ChecklistSection } from './ChecklistSection';
import { DetailsSection } from './DetailsSection';
import { HistorySection } from './HistorySection';
import { PanelHeader } from './PanelHeader';
import { PanelSection } from './PanelSection';
import { PermissionTracker } from './PermissionTracker';
import { MergeSource } from './MergeSource';
import { RemoveSource } from './RemoveSource';
import { UsedInSection } from './UsedInSection';
import { useSourceActions } from './useSourceActions';
import { VerdictBlock } from './VerdictBlock';
import { useCanWrite } from '../../../shell/bookAccess';

// A fieldset only to disable the controls inside it below write; it draws nothing.
const fieldset = 'm-0 min-w-0 border-0 p-0';

// The detail panel of one source: verdict and checklist, where it is used, its details, history, and removal.
// Every write answers with the refreshed panel and refreshes the list, the strip and the book bar badge.
export function SourcePanel({
  bookId,
  sourceId,
  onClose,
}: {
  bookId: string;
  sourceId: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const detailsRef = useRef<HTMLDivElement>(null);
  const removeRef = useRef<HTMLDivElement>(null);
  const rulesName = usePublicationSettings(bookId).data?.rulesName;
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const [merging, setMerging] = useState(false);
  const actions = useSourceActions(bookId, sourceId);
  // Below write (#80) the panel reads: the forms inside are disabled (a disabled fieldset), and Re-check, Merge and
  // Remove are not offered.
  const canWrite = useCanWrite();
  const query = useQuery({
    queryKey: sourceKey(sourceId),
    queryFn: ({ signal }) => getSource(sourceId, { signal }),
    // A source that is gone (removed, or a stale link) will not come back by asking again.
    retry: (count, error) =>
      !(error instanceof ApiRequestError && error.status === 404) && count < 3,
  });
  // The merge dialog lists the book's other sources; the list is already loaded by the screen.
  const sources = useQuery({
    queryKey: sourcesKey(bookId),
    queryFn: ({ signal }) => getSources(bookId, { signal }),
    enabled: merging,
  });

  if (query.error !== null) {
    return (
      <aside data-testid={refsTestIds.panel} className="p-4">
        <p role="alert" data-testid={refsTestIds.panelError} className="m-0 text-sm text-danger">
          {query.error.message}
        </p>
      </aside>
    );
  }
  const source: SourceDetail | undefined = query.data;
  if (source === undefined) {
    return (
      <aside data-testid={refsTestIds.panel} className="p-4">
        <span data-testid={refsTestIds.panelLoading} className="text-sm text-muted">
          Loading source…
        </span>
      </aside>
    );
  }

  const focusDetails = (field: string) => {
    detailsRef.current?.scrollIntoView({ block: 'nearest' });
    detailsRef.current
      ?.querySelector<HTMLElement>(`[data-testid="${refsTestIds.field(field)}"]`)
      ?.focus();
  };

  const act = (action: VerdictAction) => {
    switch (action) {
      case 'sign_off':
        void actions.run(() => signOffSource(sourceId));
        break;
      case 'reopen':
        void actions.run(() => reopenSource(sourceId));
        break;
      case 'recheck':
        void recheck();
        break;
      case 'set_licence':
        focusDetails('licence');
        break;
      case 'fill_details':
        focusDetails('title');
        break;
      case 'remove':
        setConfirmingRemove(true);
        break;
      default:
        break;
    }
  };

  const recheck = () => actions.run(async () => (await recheckSource(sourceId)).source);

  const merge = async (intoId: string) => {
    const target = sources.data?.find((row) => row.id === intoId);
    try {
      const merged = await mergeSource(sourceId, intoId);
      queryClient.removeQueries({ queryKey: sourceKey(sourceId) });
      queryClient.setQueryData(sourceKey(intoId), merged);
      await actions.refresh();
      showToast({ message: `Merged ${source.title} into ${target?.title ?? 'the other source'}` });
      onClose();
    } catch (caught) {
      actions.fail(
        caught instanceof ApiRequestError ? caught.message : 'The sources could not be merged.',
      );
    }
  };

  const remove = async () => {
    try {
      const result = await archiveSource(sourceId);
      queryClient.removeQueries({ queryKey: sourceKey(sourceId) });
      await actions.refresh();
      const scenes = result.scenes.length;
      showToast({
        message:
          scenes === 0
            ? `Removed ${source.title}`
            : `Removed ${source.title} · markers in ${scenes} ${scenes === 1 ? 'scene' : 'scenes'} are now plain text`,
      });
      onClose();
    } catch (caught) {
      actions.fail(
        caught instanceof ApiRequestError ? caught.message : 'The source could not be removed.',
      );
    }
  };

  return (
    <aside data-testid={refsTestIds.panel} data-source={source.id} className="flex flex-col">
      <PanelHeader
        source={source}
        onClose={onClose}
        onRemove={() => {
          setConfirmingRemove(true);
          removeRef.current?.scrollIntoView({ block: 'nearest' });
        }}
        onMerge={() => setMerging(true)}
      />
      {merging && (
        <div className="p-3">
          <MergeSource
            source={source}
            candidates={sources.data ?? []}
            busy={actions.busy}
            onCancel={() => setMerging(false)}
            onConfirm={(intoId) => void merge(intoId)}
          />
        </div>
      )}
      {actions.error !== null && (
        <p
          role="alert"
          data-testid={refsTestIds.panelBanner}
          className="m-0 border-b border-border bg-surface-2 px-4 py-2 text-sm text-danger"
        >
          {actions.error}
        </p>
      )}
      <PanelSection
        title="Clearance"
        note={`${source.checks.filter((check) => check.status === 'ok').length} of ${source.checks.length} checks pass`}
      >
        <VerdictBlock
          source={source}
          busy={actions.busy}
          onAct={act}
          onFairUse={(reason) => void actions.run(() => acceptFairUse(sourceId, reason))}
          onAskPermission={() => void actions.run(() => draftPermission(sourceId))}
        />
        {(source.permission !== null || source.verdict.action === 'permission') && (
          <fieldset disabled={!canWrite} className={fieldset}>
            <PermissionTracker
              source={source}
              busy={actions.busy}
              run={actions.run}
              fail={actions.fail}
            />
          </fieldset>
        )}
        <ChecklistSection checks={source.checks} />
        <p className="mt-2 mb-0 text-2xs text-muted">
          Checked against{' '}
          <Link
            to={settingsLink(bookId, 'rules')}
            data-testid={refsTestIds.rulesLink}
            className="text-accent no-underline hover:underline"
          >
            Publisher rules{rulesName ? `: ${rulesName}` : ''}
          </Link>{' '}
          (prose quotes up to {source.limit} words; lyrics, poetry and figures need permission). A
          guide, not legal advice.
        </p>
      </PanelSection>
      <PanelSection
        title="Used in the book"
        note={`${source.uses.length} ${source.uses.length === 1 ? 'use' : 'uses'}${
          source.quotedWords > 0 ? ` · ${source.quotedWords} words quoted` : ''
        }`}
      >
        <fieldset disabled={!canWrite} className={fieldset}>
          <UsedInSection
            source={source}
            busy={actions.busy}
            onKind={(useId, kind) => void actions.run(() => setUseKind(useId, kind))}
          />
        </fieldset>
      </PanelSection>
      <div ref={detailsRef}>
        <PanelSection
          title="Source details"
          action={
            canWrite && (
              <button
                type="button"
                disabled={actions.busy}
                data-testid={refsTestIds.recheck}
                title="Check the ISBN, DOI and URL again"
                onClick={() => void recheck()}
                className="text-xs text-accent underline"
              >
                Re-check
              </button>
            )
          }
        >
          <fieldset disabled={!canWrite} className={fieldset}>
            <DetailsSection
              source={source}
              busy={actions.busy}
              onSave={(details) =>
                actions.run(() => patchSource(sourceId, { details, entry: null }))
              }
              onEntry={(entryId) =>
                void actions.run(() => patchSource(sourceId, { details: null, entry: { entryId } }))
              }
              onAcceptRedirect={(redirectTo) =>
                void actions.run(() => acceptRedirect(sourceId, redirectTo))
              }
              onInvalid={actions.fail}
            />
          </fieldset>
        </PanelSection>
      </div>
      <PanelSection title="History">
        <HistorySection history={source.history} />
      </PanelSection>
      {canWrite && (
        <div ref={removeRef} className="p-3">
          <RemoveSource
            source={source}
            confirming={confirmingRemove}
            busy={actions.busy}
            onAsk={() => setConfirmingRemove(true)}
            onCancel={() => setConfirmingRemove(false)}
            onConfirm={() => void remove()}
          />
        </div>
      )}
    </aside>
  );
}
