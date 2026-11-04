import { useCanWrite } from '../../shell/bookAccess';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { manuscriptApi, type ProseRevision } from '@codex/core/api/manuscript';
import { invalidateOutline } from '@codex/core/api/outline';
import { manuscriptKey, setSceneProse, type Manuscript } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { useSceneRegistry } from './sceneRegistry';
import { writeTestIds } from './testIds';
import { wordDiff } from '@codex/core/features/write/wordDiff';

interface VersionHistoryProps {
  sceneId: string;
  heading: string;
  onClose: () => void;
}

const historyKey = (sceneId: string) => ['prose-history', sceneId] as const;

const plural = (n: number) => `${n.toLocaleString('en-US')} ${n === 1 ? 'word' : 'words'}`;

// "−5 words": what restoring the selected version does to the word count.
function delta(change: number) {
  if (change === 0) return '0 words';
  return `${change < 0 ? '−' : '+'}${plural(Math.abs(change))}`;
}

// The scene's version history (J15): newest first, a word-level comparison of the selected version against the text
// now, and Restore, which asks first. Esc closes it and the ⋯ button gets focus back. An archived scene's is read-only.
export function VersionHistory({ sceneId, heading, onClose }: VersionHistoryProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const registry = useSceneRegistry();
  const panel = useRef<HTMLElement>(null);
  const [settled, setSettled] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // What is typed goes to the server first, so the list holds the text on screen.
  useEffect(() => {
    let live = true;
    void (async () => {
      await registry.get(sceneId)?.settle();
      if (live) setSettled(true);
    })();
    return () => {
      live = false;
    };
  }, [registry, sceneId]);

  const history = useQuery({
    queryKey: historyKey(sceneId),
    queryFn: ({ signal }) => manuscriptApi.history(sceneId, { signal }),
    enabled: settled,
    gcTime: 0,
    staleTime: 0,
  });
  const revisions = useMemo(() => history.data?.revisions ?? [], [history.data]);
  const selected: ProseRevision | undefined =
    revisions.find((r) => r.id === selectedId) ?? revisions.find((r) => r.current) ?? revisions[0];

  const now = history.data?.body;
  const diff = useMemo(
    () => (selected && now !== undefined ? wordDiff(selected.body, now) : null),
    [selected, now],
  );

  // Another session saved since this page loaded the scene.
  const updatedElsewhere = useMemo(() => {
    const cached = queryClient
      .getQueryData<Manuscript>(manuscriptKey(bookId))
      ?.scenes.find((s) => s.sceneId === sceneId);
    return (
      cached !== undefined && history.data !== undefined && cached.version < history.data.version
    );
  }, [queryClient, bookId, sceneId, history.data]);

  useEffect(() => {
    // Focus once, when the list first arrives.
    if (history.isSuccess) {
      panel.current?.querySelector<HTMLElement>('[aria-selected="true"]')?.focus();
    }
  }, [history.isSuccess]);

  const close = () => {
    onClose();
    document
      .querySelector<HTMLElement>(`[data-testid="${writeTestIds.sceneMore(sceneId)}"]`)
      ?.focus();
  };

  // Esc closes the panel wherever focus is (a button that was just clicked away leaves it on the page).
  const closeNow = useRef(close);
  useEffect(() => {
    closeNow.current = close;
  });
  useEffect(() => {
    const onEsc = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') closeNow.current();
    };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, []);

  const adopt = async (result: { body: string; version: number }) => {
    setSceneProse(queryClient, bookId, { sceneId, body: result.body, version: result.version });
    await invalidateOutline(queryClient, bookId);
    setSelectedId(null);
    await queryClient.invalidateQueries({ queryKey: historyKey(sceneId) });
  };

  const restore = async (revision: ProseRevision) => {
    setBusy(true);
    setError(null);
    try {
      const result = await manuscriptApi.restoreRevision(sceneId, revision.id);
      setConfirming(false);
      await adopt(result);
      showToast({
        message: `Restored the version from ${revision.clockLabel}.`,
        undo: async () => {
          await adopt(await manuscriptApi.restoreRevision(sceneId, result.previousRevisionId));
        },
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The version could not be restored.');
    } finally {
      setBusy(false);
    }
  };

  const onKey = (event: KeyboardEvent) => {
    if ((event.key !== 'ArrowUp' && event.key !== 'ArrowDown') || !selected) return;
    event.preventDefault();
    const at = revisions.findIndex((r) => r.id === selected.id);
    const next = revisions[at + (event.key === 'ArrowDown' ? 1 : -1)];
    if (!next) return;
    setSelectedId(next.id);
    setConfirming(false);
    requestAnimationFrame(() =>
      panel.current
        ?.querySelector<HTMLElement>(`[data-testid="${writeTestIds.historyRow(next.id)}"]`)
        ?.focus(),
    );
  };

  const archived = history.data?.archived === true;
  // Below write (#80) history reads, but nothing is restored.
  const canWrite = useCanWrite();
  const readOnly = archived || !canWrite;
  // A row holding the very text the scene has now (the Current row, or an older one with the same text).
  const sameAsNow = selected !== undefined && selected.body === now;
  // In the body: the gutter is sticky, which would trap the panel under the chapters that follow.
  return createPortal(
    <aside
      ref={panel}
      role="dialog"
      aria-label={`Version history, ${heading}`}
      data-testid={writeTestIds.history}
      onKeyDown={onKey}
      className="fixed top-0 right-0 z-40 flex h-full w-[min(560px,100vw)] flex-col gap-3 overflow-auto border-l border-border bg-surface p-4 text-sm shadow-raised"
    >
      <div className="flex items-center gap-2">
        <h2 className="flex-1 font-semibold">Version history · {heading}</h2>
        <button
          type="button"
          data-testid={writeTestIds.historyClose}
          aria-label="Close version history"
          className="write-ib"
          onClick={close}
        >
          ×
        </button>
      </div>
      {history.error && <p role="alert">{history.error.message}</p>}
      {updatedElsewhere && (
        <p data-testid={writeTestIds.historyUpdated} className="text-warn">
          Updated elsewhere. Restoring now replaces that text; it stays in history.
        </p>
      )}
      {archived && (
        <p data-testid={writeTestIds.historyReadOnly} className="text-muted">
          This scene is archived, so its history is read-only.
        </p>
      )}
      <ul
        role="listbox"
        aria-label="Versions"
        data-testid={writeTestIds.historyList}
        className="flex flex-col gap-1"
      >
        {revisions.map((revision) => (
          <li key={revision.id} role="presentation">
            <button
              type="button"
              role="option"
              aria-selected={revision.id === selected?.id}
              tabIndex={revision.id === selected?.id ? 0 : -1}
              data-testid={writeTestIds.historyRow(revision.id)}
              data-source={revision.source}
              onClick={() => {
                setSelectedId(revision.id);
                setConfirming(false);
              }}
              className={`flex w-full flex-wrap items-baseline gap-x-2 rounded-md border px-2 py-1 text-left ${
                revision.id === selected?.id ? 'border-accent bg-accent-soft' : 'border-border'
              }`}
            >
              <span className="text-muted">{revision.timeLabel}</span>
              <span className="font-medium">{revision.label}</span>
              <span>{plural(revision.words)}</span>
              {revision.detail && <span className="text-muted">{revision.detail}</span>}
              {revision.current && (
                <span data-testid={writeTestIds.historyCurrent} className="write-chip">
                  Current
                </span>
              )}
            </button>
          </li>
        ))}
      </ul>
      {selected && diff && history.data && (
        <section aria-label="Comparison" className="flex flex-col gap-2">
          <div className="flex items-center gap-3">
            <span data-testid={writeTestIds.historyDelta} className="font-semibold">
              {delta(selected.words - history.data.currentWords)}
            </span>
            <span className="text-muted">
              <span data-testid={writeTestIds.historyAdded}>{diff.added}</span> added ·{' '}
              <span data-testid={writeTestIds.historyRemoved}>{diff.removed}</span> removed compared
              with now
            </span>
          </div>
          <div
            data-testid={writeTestIds.historyCompare}
            className="max-h-[40vh] overflow-auto rounded-md border border-border p-2 whitespace-pre-wrap"
          >
            {diff.parts.map((part, i) =>
              part.kind === 'same' ? (
                <span key={i}>{part.text}</span>
              ) : part.kind === 'added' ? (
                <ins
                  key={i}
                  className="bg-accent-soft no-underline"
                  title="In the current text only"
                >
                  {part.text}
                </ins>
              ) : (
                <del key={i} className="text-danger" title="In this version only">
                  {part.text}
                </del>
              ),
            )}
          </div>
          {!readOnly &&
            (confirming ? (
              <div className="flex flex-wrap items-center gap-2">
                <span data-testid={writeTestIds.historyConfirmText}>
                  Replaces the current text with the version from {selected.clockLabel}. The current
                  text stays in history.
                </span>
                <button
                  type="button"
                  data-testid={writeTestIds.historyConfirm}
                  disabled={busy}
                  className="write-chip write-chip-primary"
                  onClick={() => void restore(selected)}
                >
                  Restore
                </button>
                <button
                  type="button"
                  data-testid={writeTestIds.historyCancel}
                  className="write-chip"
                  onClick={() => setConfirming(false)}
                >
                  Cancel
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  data-testid={writeTestIds.historyRestore}
                  disabled={sameAsNow}
                  title={sameAsNow ? 'This is the current version' : undefined}
                  className="write-chip write-chip-primary"
                  onClick={() => setConfirming(true)}
                >
                  Restore this version
                </button>
                {selected.current && (
                  <span className="text-muted">This is the current version</span>
                )}
              </div>
            ))}
          {error && <p role="alert">{error}</p>}
        </section>
      )}
    </aside>,
    document.body,
  );
}
