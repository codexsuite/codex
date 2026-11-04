import { useEffect, useMemo, useState, type RefObject } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import {
  findKey,
  findReplaceApi,
  replaceMessage,
  type FindItem,
  type FindOptions,
} from '@codex/core/api/findReplace';
import { invalidateOutline } from '@codex/core/api/outline';
import { markersKey } from '@codex/core/api/references';
import { beatsKey, manuscriptKey, writeApi } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { useSceneRegistry } from './sceneRegistry';
import type { Scope } from '@codex/core/features/write/scope';
import { writeTestIds } from './testIds';

export interface FindFlags {
  matchCase: boolean;
  wholeWord: boolean;
}

interface FindReplacePanelProps {
  scroller: RefObject<HTMLElement | null>;
  scope: Scope;
  headings: ReadonlyMap<string, string>;
  /** The toolbar's find text (trimmed). */
  query: string;
  flags: FindFlags;
  onFlags: (flags: FindFlags) => void;
  onClose: () => void;
}

const LISTED = 50;
const DEBOUNCE_MS = 250;

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

const reasons: Record<FindItem['status'], string> = {
  replaceable: '',
  cited: 'Inside a cited passage',
  spans_link: 'Spans a link',
};

// Replace, under the toolbar's find box: where to search (prose, summaries, beat instructions), how, and the matches
// from the server, counted over the whole scope whether or not their scenes are drawn. Replace changes the match you
// are on, Replace all every one it may; words inside a cited passage and matches that span a link are shown and
// skipped, and a pending AI draft is counted and left alone. One Undo takes a Replace all back.
export function FindReplacePanel({
  scroller,
  scope,
  headings,
  query,
  flags,
  onFlags,
  onClose,
}: FindReplacePanelProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const registry = useSceneRegistry();
  const [replacement, setReplacement] = useState('');
  const [what, setWhat] = useState({ prose: true, summaries: false, beats: false });
  const [includeArchived, setIncludeArchived] = useState(false);
  const [cursor, setCursor] = useState(0);
  const [busy, setBusy] = useState(false);

  const wanted = useDebounced(query, DEBOUNCE_MS);
  const options = useMemo<FindOptions>(
    () => ({
      query: wanted,
      scopeKind: scope.kind,
      scopeId: scope.kind === 'book' ? null : scope.id,
      ...what,
      matchCase: flags.matchCase,
      wholeWord: flags.wholeWord,
      includeArchived,
    }),
    [wanted, scope, what, flags, includeArchived],
  );
  const searchable = wanted !== '' && (what.prose || what.summaries || what.beats);
  const found = useQuery({
    queryKey: findKey(bookId, options),
    queryFn: ({ signal }) => findReplaceApi.find(bookId, options, { signal }),
    enabled: searchable,
    gcTime: 0,
  });
  const result = searchable ? found.data : undefined;
  const items = result?.items ?? [];
  const at = Math.min(cursor, Math.max(0, items.length - 1));
  const current: FindItem | undefined = items[at];

  const same = replacement === query;
  const canReplaceAll = !same && (result?.replaceable ?? 0) > 0 && !busy;
  const canReplaceOne = !same && current?.status === 'replaceable' && !busy;

  const refresh = async () => {
    await Promise.all([
      // Read again and put in the cache: a scene's editor adopts the server's newer text from there. (Not an
      // invalidation: a draft panel also observes this query without a way to fetch it.)
      writeApi
        .manuscript(bookId)
        .then((fresh) => queryClient.setQueryData(manuscriptKey(bookId), fresh)),
      queryClient.invalidateQueries({ queryKey: beatsKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: markersKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: ['find', bookId] }),
      invalidateOutline(queryClient, bookId),
    ]);
  };

  const run = async (only: FindItem | null) => {
    setBusy(true);
    try {
      // What the server replaces in is the saved text, so unsaved typing goes first.
      if (!(await registry.settleAll())) {
        showToast({ message: 'Some text could not be saved, so nothing was replaced.' });
        return;
      }
      const done = await findReplaceApi.replace(
        bookId,
        { ...options, query: query },
        replacement,
        only ? { sceneId: only.sceneId, source: only.source, ordinal: only.ordinal } : null,
      );
      showToast({
        message: replaceMessage(done),
        undo:
          done.replaced > 0
            ? async () => {
                await findReplaceApi.undo(bookId, done.undo);
                await refresh();
              }
            : undefined,
      });
      await refresh();
    } catch (error) {
      showToast({ message: error instanceof Error ? error.message : 'The replace failed.' });
    } finally {
      setBusy(false);
    }
  };

  const show = (index: number) => {
    const total = items.length;
    if (total === 0) return;
    const next = (index + total) % total;
    setCursor(next);
    const item = items[next];
    scroller.current
      ?.querySelector<HTMLElement>(`[data-scene-id="${item.sceneId}"]`)
      ?.scrollIntoView({ block: 'center' });
  };

  const count =
    result === undefined
      ? ''
      : result.matches === 0
        ? 'No matches'
        : `${result.matches} ${result.matches === 1 ? 'match' : 'matches'} in ${result.scenes} ${
            result.scenes === 1 ? 'scene' : 'scenes'
          }` +
          (result.cited + result.spansLink > 0
            ? ` · ${result.replaceable} can be replaced, ${result.cited + result.spansLink} locked`
            : '');

  const toggle = (key: keyof typeof what) => (
    <input
      type="checkbox"
      data-testid={
        key === 'prose'
          ? writeTestIds.replaceProse
          : key === 'summaries'
            ? writeTestIds.replaceSummaries
            : writeTestIds.replaceBeats
      }
      checked={what[key]}
      onChange={(event) => {
        setWhat({ ...what, [key]: event.target.checked });
        setCursor(0);
      }}
    />
  );

  return (
    <div
      role="dialog"
      aria-label="Replace"
      data-testid={writeTestIds.replacePanel}
      className="absolute top-full right-0 z-30 mt-1 flex w-[min(420px,90vw)] flex-col gap-2 rounded-md border border-border bg-surface p-3 text-sm shadow-raised"
    >
      <div className="flex items-center gap-2">
        <input
          type="text"
          data-testid={writeTestIds.replaceInput}
          aria-label="Replace with"
          placeholder="Replace with…"
          className="min-w-0 flex-1 rounded-sm border border-border bg-surface px-2 py-1"
          value={replacement}
          onChange={(event) => setReplacement(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && canReplaceOne) {
              event.preventDefault();
              void run(current ?? null);
            }
          }}
        />
        <button
          type="button"
          data-testid={writeTestIds.replaceClose}
          aria-label="Close replace"
          className="write-ib"
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <fieldset className="flex flex-wrap gap-x-3 gap-y-1">
        <legend className="text-muted">Search in</legend>
        <label className="flex items-center gap-1">{toggle('prose')}Prose</label>
        <label className="flex items-center gap-1">{toggle('summaries')}Summaries</label>
        <label className="flex items-center gap-1">{toggle('beats')}Beat instructions</label>
      </fieldset>
      <div className="flex flex-wrap gap-x-3 gap-y-1">
        <label className="flex items-center gap-1">
          <input
            type="checkbox"
            data-testid={writeTestIds.replaceMatchCase}
            checked={flags.matchCase}
            onChange={(event) => onFlags({ ...flags, matchCase: event.target.checked })}
          />
          Match case
        </label>
        <label className="flex items-center gap-1">
          <input
            type="checkbox"
            data-testid={writeTestIds.replaceWholeWord}
            checked={flags.wholeWord}
            onChange={(event) => onFlags({ ...flags, wholeWord: event.target.checked })}
          />
          Whole word
        </label>
        <label className="flex items-center gap-1">
          <input
            type="checkbox"
            data-testid={writeTestIds.replaceArchived}
            checked={includeArchived}
            onChange={(event) => setIncludeArchived(event.target.checked)}
          />
          Include archived scenes
        </label>
      </div>
      <div data-testid={writeTestIds.replaceCount} role="status" className="text-muted">
        {count}
      </div>
      {result !== undefined && result.inDrafts > 0 && (
        <div data-testid={writeTestIds.replaceDrafts} className="text-muted italic">
          {result.inDrafts} in a pending draft. Replace all skips them.
        </div>
      )}
      {items.length > 0 && (
        <ul className="max-h-40 overflow-auto rounded-sm border border-border">
          {items.slice(0, LISTED).map((item, index) => (
            <li key={`${item.sceneId}-${item.source}-${item.ordinal}`}>
              <button
                type="button"
                data-testid={writeTestIds.replaceItem(index)}
                data-status={item.status}
                aria-current={index === at}
                className={`flex w-full gap-2 px-2 py-1 text-left ${index === at ? 'bg-accent-soft' : ''} ${
                  item.status === 'replaceable' ? '' : 'text-muted'
                }`}
                onClick={() => show(index)}
              >
                <span className="flex-none text-muted">
                  {headings.get(item.sceneId) ?? 'Archived scene'}
                  {item.source === 'prose' ? '' : ` · ${item.source}`}
                </span>
                <span className="min-w-0 flex-1 truncate">
                  …{item.before}
                  <b>{item.text}</b>
                  {item.after}…
                </span>
                {item.status !== 'replaceable' && (
                  <span className="flex-none" title={reasons[item.status]}>
                    Locked: {reasons[item.status].toLowerCase()}
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-2">
        <button
          type="button"
          data-testid={writeTestIds.replacePrevious}
          className="write-chip"
          disabled={items.length === 0}
          onClick={() => show(at - 1)}
        >
          Previous
        </button>
        <button
          type="button"
          data-testid={writeTestIds.replaceNext}
          className="write-chip"
          disabled={items.length === 0}
          onClick={() => show(at + 1)}
        >
          Next
        </button>
        <span className="flex-1" />
        <button
          type="button"
          data-testid={writeTestIds.replaceOne}
          className="write-chip"
          disabled={!canReplaceOne}
          onClick={() => void run(current ?? null)}
        >
          Replace
        </button>
        <button
          type="button"
          data-testid={writeTestIds.replaceAll}
          className="write-chip write-chip-primary"
          disabled={!canReplaceAll}
          onClick={() => void run(null)}
        >
          Replace all
        </button>
      </div>
    </div>
  );
}
