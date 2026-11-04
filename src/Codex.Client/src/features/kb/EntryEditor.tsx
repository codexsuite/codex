import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiRequestError } from '@codex/core/api/client';
import { createKbCategory, type KbCustomField, type KbEntry } from '@codex/core/api/kbEntries';
import { useBook } from '../../api/booksQueries';
import { kbKey } from '@codex/core/api/kb';
import { useAutosave } from '../../editing/useAutosave';
import type { MentionEntry } from '../../prose';
import { useKbEntries } from './useKbEntries';
import { DetailsFields } from './DetailsFields';
import { AliasList } from './AliasList';
import { EntryFrame } from './EntryFrame';
import { EntryHeader } from './EntryHeader';
import { EntryTab } from './EntryTab';
import { CopyIcon } from './icons';
import { iconButton } from './kbStyles';
import { Rail } from './Rail';
import { TimelineTab } from './TimelineTab';
import { UsedInTab } from './UsedInTab';
import { kbTestIds } from './testIds';
import { useEntryPatch } from './useEntryPatch';
import { useEntryReorder } from './useEntryReorder';
import { useKbCategories } from './useKbCategories';
import { useKbSidebar } from './useKbSidebar';

export type TabName = 'entry' | 'used-in' | 'timeline';

interface EntryEditorProps {
  bookId: string;
  entry: KbEntry;
  /** The selected tab and the window size live above the entry, so stepping to another entry keeps both. */
  tab: TabName;
  expanded: boolean;
  onTab: (tab: TabName) => void;
  onExpanded: (expanded: boolean) => void;
  /** Open another entry (previous / next). */
  onStep: (entryId: string) => void;
  onClose: () => void;
}

interface Content {
  name: string;
  summary: string;
  body: string;
  fields: Record<string, string>;
  customFields: KbCustomField[];
}

const tabLabels: Record<TabName, string> = {
  entry: 'Entry',
  'used-in': 'Used in',
  timeline: 'Timeline',
};

const contentOf = (entry: KbEntry): Content => ({
  name: entry.name,
  summary: entry.summary,
  body: entry.body,
  fields: entry.fields,
  customFields: entry.customFields,
});

// One entry: header, aliases, the Entry / Used in / Timeline tabs and the rail. The text parts autosave together
// (3 s, on blur, on close); aliases, category and the context switch save at once. The entry is edited in local
// state; the server's answer only refreshes the parts the author is not typing in.
export function EntryEditor({
  bookId,
  entry,
  tab,
  expanded,
  onTab,
  onExpanded,
  onStep,
  onClose,
}: EntryEditorProps) {
  const queryClient = useQueryClient();
  const book = useBook(bookId);
  const patch = useEntryPatch(bookId, entry.id);
  const categories = useKbCategories(bookId);
  const mentions = useKbEntries(bookId);
  const { sections } = useKbSidebar(bookId, '');
  const [content, setContent] = useState<Content>(() => contentOf(entry));
  const [error, setError] = useState<string | null>(null);
  // A rename in progress: keep the old name as an alias (on by default), and whether another entry blocked the new name.
  const [keepOldName, setKeepOldName] = useState(true);
  const [renameBlocked, setRenameBlocked] = useState(false);

  const autosave = useAutosave<Content>({
    value: content,
    version: 0,
    save: async (value) => {
      // A custom field row with no name yet is not a field; it is saved once it has one.
      // A rename saves on its own (commitName); the timer leaves a name that is still being typed out of its request.
      await patch({
        name: value.name === entry.name ? entry.name : undefined,
        summary: value.summary,
        body: value.body,
        fields: value.fields,
        customFields: value.customFields.filter((f) => f.label.trim() !== ''),
      });
      return { version: 0 };
    },
    // Refused as forbidden (#80): a series entry needs write on the series, which a grant on one book does not give.
    // The autosave has said why; put the entry's saved text back.
    onForbidden: () => setContent(contentOf(entry)),
  });
  useEntryReorder(bookId, entry.id);

  const mentionEntries = useMemo<MentionEntry[]>(
    () => mentions.filter((m) => m.id !== entry.id),
    [mentions, entry.id],
  );
  const edit = (change: Partial<Content>) => setContent((c) => ({ ...c, ...change }));

  const guard = async (action: () => Promise<unknown>) => {
    try {
      await action();
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That change could not be saved.');
      throw e;
    }
  };

  const renaming = content.name.trim() !== '' && content.name.trim() !== entry.name;

  // Enter or leaving the name saves it. Empty keeps the old name; a name another entry has waits for Rename anyway.
  const commitName = async (allowDuplicate = false) => {
    const name = content.name.trim();
    if (name === '') {
      edit({ name: entry.name });
      setError('An entry needs a name.');
      return;
    }
    if (name === entry.name) return;
    try {
      await patch({ name, keepOldName, allowDuplicate });
      setError(null);
      setRenameBlocked(false);
      setKeepOldName(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The name could not be saved.');
      setRenameBlocked(e instanceof ApiRequestError && e.status === 409);
    }
  };
  const focusTo = (next: EventTarget | null, testId: string) =>
    next instanceof HTMLElement && next.dataset.testid === testId;

  const close = () => {
    autosave.flush();
    onClose();
  };

  // Previous / next follow the sidebar's order, across categories.
  const order = sections.flatMap((section) =>
    section.groups.flatMap((group) => group.entries.map((e) => e.id)),
  );
  const index = order.indexOf(entry.id);
  const step = (id: string) => {
    autosave.flush();
    onStep(id);
  };
  const counts: Record<TabName, number | null> = {
    entry: null,
    'used-in': entry.usedIn.length,
    timeline: entry.timeline.length,
  };
  const copyId = () => {
    void navigator.clipboard?.writeText(`e:${entry.slug}`).catch(() => undefined);
  };

  return (
    <EntryFrame
      label={content.name}
      colour={entry.categoryColour}
      expanded={expanded}
      onClose={close}
    >
      <div className="flex min-h-0 flex-1 flex-col" onBlur={() => autosave.flush()}>
        <EntryHeader
          bookId={bookId}
          entryId={entry.id}
          name={content.name}
          categoryName={entry.categoryName}
          categoryColour={entry.categoryColour}
          categories={categories}
          position={`${entry.positionInCategory} of ${entry.categoryEntryCount}`}
          previousId={index > 0 ? order[index - 1] : null}
          nextId={index >= 0 && index < order.length - 1 ? order[index + 1] : null}
          expanded={expanded}
          onName={(name) => edit({ name })}
          onNameBlur={(next) => {
            if (!focusTo(next, kbTestIds.renameKeepAlias)) void commitName();
          }}
          onPickCategory={(categoryId) => guard(() => patch({ categoryId }))}
          onCreateCategory={async (name, colour) => {
            const created = await createKbCategory(bookId, { name, colour });
            await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
            await patch({ categoryId: created.id });
          }}
          onStep={step}
          onToggleExpand={() => onExpanded(!expanded)}
          onClose={close}
          onDeleted={onClose}
          inSeries={entry.bookId !== bookId}
          seriesName={
            entry.bookId === bookId && book.data?.seriesId
              ? (book.data.seriesName ?? 'the series')
              : undefined
          }
          onPromoted={(merged) => {
            if (merged) onClose();
          }}
        />
        {renaming && (
          <label className="flex shrink-0 items-center gap-1.5 px-4 pt-1 text-xs text-muted">
            <input
              type="checkbox"
              data-testid={kbTestIds.renameKeepAlias}
              checked={keepOldName}
              onChange={(event) => setKeepOldName(event.target.checked)}
              onBlur={(event) => {
                if (!focusTo(event.relatedTarget, kbTestIds.entryName)) void commitName();
              }}
            />
            Keep &quot;{entry.name}&quot; as an alias
          </label>
        )}
        <AliasList
          entryId={entry.id}
          aliases={entry.aliases}
          onChange={(aliases, allowDuplicate) => patch({ aliases, allowDuplicate })}
        />
        {error && (
          <p
            data-testid={kbTestIds.entryError}
            role="alert"
            className="m-0 px-4 text-xs text-danger"
          >
            {error}
            {renameBlocked && (
              <button
                type="button"
                data-testid={kbTestIds.renameAnyway}
                onClick={() => void commitName(true)}
                className="ml-2 h-5 rounded-xs border border-border bg-transparent px-1.5 text-xs text-text hover:bg-surface-2"
              >
                Rename anyway
              </button>
            )}
          </p>
        )}
        <div role="tablist" className="flex shrink-0 items-end gap-0.5 border-b border-border px-3">
          {(Object.keys(tabLabels) as TabName[]).map((name) => (
            <button
              key={name}
              type="button"
              role="tab"
              data-testid={kbTestIds.tab(name)}
              aria-selected={tab === name}
              onClick={() => onTab(name)}
              className="relative inline-flex h-8 items-center gap-1.5 border-0 bg-transparent px-2 text-sm font-medium whitespace-nowrap text-muted hover:text-text aria-selected:font-semibold aria-selected:text-text aria-selected:after:absolute aria-selected:after:right-1.5 aria-selected:after:-bottom-px aria-selected:after:left-1.5 aria-selected:after:h-0.5 aria-selected:after:rounded-[2px] aria-selected:after:bg-accent aria-selected:after:content-['']"
            >
              {tabLabels[name]}
              {counts[name] !== null && (
                <span
                  data-testid={kbTestIds.tabCount(name)}
                  className="h-4 min-w-4 rounded-lg bg-surface-2 px-[5px] text-center text-2xs leading-4 font-semibold tabular-nums"
                >
                  {counts[name]}
                </span>
              )}
            </button>
          ))}
          <span className="flex-1" />
          <span
            title="Reference id: stays the same when the entry is renamed"
            className="inline-flex min-w-0 items-center gap-0.5 self-center overflow-hidden font-mono text-2xs whitespace-nowrap text-muted @max-[680px]:hidden"
          >
            <span data-testid={kbTestIds.referenceId}>e:{entry.slug}</span>
            <button
              type="button"
              data-testid={kbTestIds.copyReferenceId}
              aria-label="Copy reference id"
              title="Copy reference id"
              onClick={copyId}
              className={iconButton}
            >
              <CopyIcon />
            </button>
          </span>
        </div>
        <div className="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_var(--gutter-width)] items-start overflow-auto @max-[680px]:grid-cols-1">
          <div
            role="tabpanel"
            data-testid={kbTestIds.panel(tab)}
            className={`min-w-0 pt-4 pr-5 pb-5 @max-[680px]:pr-4 ${
              expanded ? 'pl-[max(var(--space-4),calc((100%-var(--prose-width))/2))]' : 'pl-4'
            }`}
          >
            {tab === 'entry' && (
              <EntryTab
                summary={content.summary}
                body={content.body}
                editedLabel={entry.updatedLabel}
                entries={mentionEntries}
                onSummary={(summary) => edit({ summary })}
                onBody={(body) => edit({ body })}
              />
            )}
            {tab === 'used-in' && (
              <UsedInTab bookId={bookId} entryId={entry.id} uses={entry.usedIn} />
            )}
            {tab === 'timeline' && (
              <TimelineTab
                bookId={bookId}
                entryId={entry.id}
                notes={entry.timeline}
                usedIn={entry.usedIn}
              />
            )}
          </div>
          <Rail
            bookId={bookId}
            entry={entry}
            onAlwaysInContext={(alwaysInContext) => void guard(() => patch({ alwaysInContext }))}
            details={
              <DetailsFields
                defs={entry.fieldDefs}
                values={content.fields}
                custom={content.customFields}
                onValue={(key, value) => edit({ fields: { ...content.fields, [key]: value } })}
                onCustom={(customFields) => edit({ customFields })}
              />
            }
          />
        </div>
      </div>
    </EntryFrame>
  );
}
