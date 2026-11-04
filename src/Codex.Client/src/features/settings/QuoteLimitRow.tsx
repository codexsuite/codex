import type { PublicationSettings } from '@codex/core/api/settings';
import { defaultLimit, parseLimit } from '@codex/core/features/settings/publicationDraft';
import { FieldRow } from './FieldRow';
import { RulesImpactStrip } from './RulesImpactStrip';
import { SourceChip } from './SourceChip';
import { settingsTestIds } from './testIds';
import { useDebouncedValue } from './useDebouncedValue';
import type { PublicationState } from './usePublication';

const input = 'h-8 w-24 rounded border border-border bg-bg px-2 text-base text-text';

// "Up to N words from one source without permission", with its effect on References worked out live while typing.
export function QuoteLimitRow({
  settings,
  state,
}: {
  settings: PublicationSettings;
  state: PublicationState;
}) {
  const limit = parseLimit(state.fields.limit);
  const typed = useDebouncedValue(limit, 250);
  return (
    <FieldRow
      label="Quoted prose"
      htmlFor="settings-quote-limit"
      chip={
        <SourceChip
          field="quoteWordLimit"
          inSeries={settings.seriesName !== null}
          inherited={settings.quoteWordLimit.inherited}
          onReset={state.reset}
        />
      }
      help={
        limit === null ? (
          <span data-testid={settingsTestIds.quoteLimitError} role="alert" className="text-danger">
            The limit must be a whole number above 0.
          </span>
        ) : (
          'Every source in this book is rechecked when this changes; changing it back undoes it.'
        )
      }
    >
      <span className="text-sm text-muted">Up to</span>
      <input
        id="settings-quote-limit"
        data-testid={settingsTestIds.quoteLimit}
        type="number"
        min={1}
        step={50}
        value={state.fields.limit}
        onChange={(e) => state.edit({ limit: e.target.value })}
        onBlur={state.flush}
        className={input}
      />
      <span className="text-sm text-muted">words from one source without permission</span>
      {settings.quoteWordLimit.value !== defaultLimit && (
        <button
          type="button"
          data-testid={settingsTestIds.quoteLimitReset}
          onClick={() => void state.choose({ quoteWordLimit: defaultLimit })}
          className="h-6 cursor-pointer rounded border border-border bg-surface px-2 text-xs text-text"
        >
          Reset to {defaultLimit}
        </button>
      )}
      <RulesImpactStrip bookId={settings.bookId} limit={typed} />
    </FieldRow>
  );
}
