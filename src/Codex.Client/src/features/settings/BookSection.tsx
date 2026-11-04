import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { bookKey } from '@codex/core/api/books';
import { settingsKey, type BookSettings } from '@codex/core/api/settings';
import { PublishDialog } from './PublishDialog';
import { CoverRow } from './CoverRow';
import type { BookDraftState } from './useBookDraft';
import { FieldRow } from './FieldRow';
import { SettingsCard } from './SettingsCard';
import { StatusSegments } from './StatusSegments';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';

const input =
  'h-[var(--control-height)] min-w-0 rounded-sm border border-border bg-bg px-2 text-sm text-text focus:border-transparent focus:outline-2 focus:-outline-offset-1 focus:outline-accent';

// Title, author, status, target words with the words / target bar, and the cover. `settings` is the server's view
// (for the bar); the fields themselves are the draft.
export function BookSection({
  settings,
  state,
}: {
  settings: BookSettings;
  state: BookDraftState;
}) {
  const queryClient = useQueryClient();
  const { draft, set, choose, flush } = state;
  const [publishing, setPublishing] = useState(false);
  const titleBlank = draft.title.trim() === '';
  const percent = settings.progressPercent;
  const { isOwner, canEdit, ownerName } = useSettingsAccess(settings.id);
  const editor = canEdit && !isOwner;
  return (
    <SettingsCard
      section="book"
      title="Book"
      blurb="How the book is named in Library and on its title page."
    >
      <FieldRow
        label="Title"
        htmlFor="settings-title"
        help={
          titleBlank && (
            <span data-testid={settingsTestIds.titleError} role="alert" className="text-danger">
              A book needs a title.
            </span>
          )
        }
      >
        <input
          id="settings-title"
          data-testid={settingsTestIds.title}
          value={draft.title}
          autoComplete="off"
          onChange={(e) => set({ title: e.target.value })}
          onBlur={flush}
          className={`${input} flex-[1_1_260px]`}
        />
      </FieldRow>
      <FieldRow
        label="Author"
        htmlFor="settings-author"
        help="As printed on the cover, title page and permission letters. A pen name is fine; you stay the owner."
      >
        <input
          id="settings-author"
          data-testid={settingsTestIds.author}
          value={draft.authorName}
          autoComplete="off"
          onChange={(e) => set({ authorName: e.target.value })}
          onBlur={flush}
          className={`${input} flex-[1_1_260px]`}
        />
      </FieldRow>
      <FieldRow
        label="Status"
        help={`Shown on the Library tile and used to sort it. Published books keep their print settings pinned.${editor ? ' Only the owner can mark it Published.' : ''}`}
      >
        <StatusSegments
          value={draft.status}
          ownerOnly={
            isOwner
              ? undefined
              : `Only ${ownerName}, the owner, can publish this book or start a new edition`
          }
          onChange={(status) =>
            // Published asks first (and takes a snapshot); every other status saves at once.
            status === 'published' && draft.status !== 'published'
              ? setPublishing(true)
              : choose({ status })
          }
        />
      </FieldRow>
      <FieldRow label="Target words" htmlFor="settings-target">
        <input
          id="settings-target"
          data-testid={settingsTestIds.target}
          type="number"
          min={0}
          step={1000}
          value={draft.targetWords}
          onChange={(e) => set({ targetWords: e.target.value })}
          onBlur={flush}
          className={`${input} w-[104px] text-right tabular-nums`}
        />
        <span className="text-xs text-muted">words</span>
        {percent !== null && settings.targetWords !== null && (
          <div
            data-testid={settingsTestIds.progress}
            className="flex basis-full items-center gap-2 text-xs text-muted tabular-nums"
          >
            <span className="block h-[3px] flex-[0_1_220px] overflow-hidden rounded-xs bg-surface-2">
              <i
                className="block h-full bg-accent"
                style={{ width: `${Math.min(percent, 100)}%` }}
              />
            </span>
            <span data-testid={settingsTestIds.progressText} className="whitespace-nowrap">
              {settings.wordCount.toLocaleString('en-US')} of{' '}
              {settings.targetWords.toLocaleString('en-US')} · {percent}%
            </span>
          </div>
        )}
      </FieldRow>
      <CoverRow bookId={settings.id} title={settings.title} />
      {publishing && (
        <PublishDialog
          settings={settings}
          onClose={() => setPublishing(false)}
          onPublished={() => {
            // The server already holds the status; the draft follows it (the save that follows is a no-op).
            set({ status: 'published' });
            setPublishing(false);
            void queryClient.invalidateQueries({ queryKey: settingsKey(settings.id) });
            void queryClient.invalidateQueries({ queryKey: bookKey(settings.id) });
            void queryClient.invalidateQueries({ queryKey: ['library'] });
          }}
        />
      )}
    </SettingsCard>
  );
}
