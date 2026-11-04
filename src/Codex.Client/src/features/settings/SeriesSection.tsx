import { useState } from 'react';
import type { BookSettings } from '@codex/core/api/settings';
import { FieldRow } from './FieldRow';
import { ReviewMoveDialog } from './ReviewMoveDialog';
import { SettingsCard } from './SettingsCard';
import { SeriesOrderList } from './SeriesOrderList';
import { numberLine, padded, wasNumbers } from '@codex/core/features/settings/seriesNumbers';
import { settingsTestIds } from './testIds';
import { useSeriesNumber } from './useSeriesNumber';
import { LockIcon } from './accessIcons';
import { useSettingsAccess } from './useSettingsAccess';

const select =
  'h-[var(--control-height)] min-w-0 cursor-pointer disabled:cursor-not-allowed rounded-sm border border-border bg-bg pr-1 pl-2 text-sm text-text focus:border-transparent focus:outline-2 focus:-outline-offset-1 focus:outline-accent';
const none = '';

// Series select and number. A number change inside the series applies at once (toast with Undo); another series
// or none shows the Not moved yet note and Review move…, which drives the copy-down.
export function SeriesSection({ settings }: { settings: BookSettings }) {
  const number = useSeriesNumber(settings);
  // Moving the book and renumbering the series are the owner's (#80).
  const { isOwner, canEdit, ownerName } = useSettingsAccess(settings.id);
  // undefined = no move chosen; null = leave the series.
  const [pending, setPending] = useState<string | null | undefined>(undefined);
  const [reviewing, setReviewing] = useState(false);
  const current = settings.seriesId;
  const shown = pending === undefined ? current : pending;
  const targetName =
    pending === null || pending === undefined
      ? 'no series'
      : (settings.seriesOptions.find((s) => s.id === pending)?.name ?? 'that series');

  const choose = (value: string) => {
    const id = value === none ? null : value;
    setPending(id === current ? undefined : id);
  };

  return (
    <SettingsCard
      section="series"
      title="Series"
      blurb="A book in a series shares the series' Knowledge base, labels, style guide and publication settings."
    >
      <FieldRow label="Series" htmlFor="settings-series">
        <select
          id="settings-series"
          data-testid={settingsTestIds.seriesSelect}
          value={shown ?? none}
          disabled={!isOwner}
          title={isOwner ? undefined : 'Only the owner can move this book to another series'}
          onChange={(e) => choose(e.target.value)}
          className={select}
        >
          <option value={none}>None (standalone)</option>
          {settings.seriesOptions.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>
        {current !== null && pending === undefined && settings.seriesNumber !== null && (
          <>
            <label htmlFor="settings-number" className="text-xs text-muted">
              Number
            </label>
            <select
              id="settings-number"
              data-testid={settingsTestIds.numberSelect}
              value={settings.seriesNumber}
              disabled={!isOwner}
              title={isOwner ? undefined : 'Only the owner can renumber the series'}
              onChange={(e) => void number.set(Number(e.target.value))}
              className={select}
            >
              {settings.seriesOrder.map((entry, index) => (
                <option key={entry.id} value={index + 1}>
                  {padded(index + 1)}
                </option>
              ))}
            </select>
            <span data-testid={settingsTestIds.numberLine} className="text-sm text-muted">
              {numberLine(settings.seriesOrder, settings.seriesNumber)}
            </span>
          </>
        )}
        {canEdit && !isOwner && (
          <p
            data-testid={settingsTestIds.ownerOnly}
            className="m-0 flex basis-full items-center gap-[5px] text-xs text-muted"
          >
            <LockIcon className="size-[11px]" />
            Only {ownerName}, the owner, can move this book or change its number.
          </p>
        )}
        {number.error && (
          <p
            data-testid={settingsTestIds.numberError}
            role="alert"
            className="m-0 w-full text-sm text-danger"
          >
            {number.error}
          </p>
        )}
      </FieldRow>
      {current === null && pending === undefined && (
        <FieldRow
          label=""
          help={
            <span data-testid={settingsTestIds.standaloneHelp}>
              Standalone. Pick a series to share its Knowledge base and settings with its other
              books.
            </span>
          }
        />
      )}
      {current !== null && pending === undefined && (
        <FieldRow label="Order">
          <SeriesOrderList
            order={settings.seriesOrder}
            currentId={settings.id}
            was={wasNumbers(number.change)}
          />
        </FieldRow>
      )}
      {pending !== undefined && (
        <div
          data-testid={settingsTestIds.moveNote}
          className="my-3 flex flex-wrap items-center gap-2 rounded border border-warn px-3 py-2 text-sm"
        >
          <span className="font-medium text-warn">Not moved yet</span>
          <span className="flex-1 text-muted">
            {settings.title} stays in {settings.seriesName ?? 'no series'} until you review the move
            to {targetName}.
          </span>
          <button
            type="button"
            data-testid={settingsTestIds.moveReview}
            onClick={() => setReviewing(true)}
            className="h-8 rounded border border-border bg-transparent px-3 text-sm text-text"
          >
            Review move…
          </button>
        </div>
      )}
      {reviewing && pending !== undefined && (
        <ReviewMoveDialog
          bookId={settings.id}
          target={pending}
          onClose={() => setReviewing(false)}
          onMoved={() => {
            setReviewing(false);
            setPending(undefined);
          }}
        />
      )}
    </SettingsCard>
  );
}
