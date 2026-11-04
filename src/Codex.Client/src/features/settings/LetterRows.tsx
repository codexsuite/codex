import type { PublicationFormat, PublicationSettings } from '@codex/core/api/settings';
import { formatOptions, territories } from '@codex/core/features/settings/publicationDraft';
import { FieldRow } from './FieldRow';
import { SourceChip } from './SourceChip';
import { settingsTestIds } from './testIds';
import type { PublicationState } from './usePublication';

const input = 'h-8 rounded border border-border bg-bg px-2 text-base text-text';

// Publisher, year, formats, first print run and territory: what every drafted permission request says.
export function LetterRows({
  settings,
  state,
}: {
  settings: PublicationSettings;
  state: PublicationState;
}) {
  const inSeries = settings.seriesName !== null;
  const { fields } = state;
  const formats = settings.formats.value;
  const toggle = (code: PublicationFormat) =>
    void state.choose({
      formats: formats.includes(code) ? formats.filter((f) => f !== code) : [...formats, code],
    });
  const rights =
    territories.includes(fields.territory) || fields.territory === ''
      ? territories
      : [fields.territory, ...territories];
  const chip = (field: Parameters<typeof SourceChip>[0]['field'], inherited: boolean) => (
    <SourceChip field={field} inSeries={inSeries} inherited={inherited} onReset={state.reset} />
  );

  return (
    <>
      <FieldRow
        label="Publisher"
        htmlFor="settings-publisher"
        chip={chip('publisher', settings.publisher.inherited)}
      >
        <input
          id="settings-publisher"
          data-testid={settingsTestIds.publisher}
          value={fields.publisher}
          placeholder="Leave blank if you publish it yourself"
          onChange={(e) => state.edit({ publisher: e.target.value })}
          onBlur={state.flush}
          className={`${input} w-full`}
        />
      </FieldRow>
      <FieldRow
        label="Publication year"
        htmlFor="settings-planned-year"
        chip={chip('plannedYear', settings.plannedYear.inherited)}
      >
        <input
          id="settings-planned-year"
          data-testid={settingsTestIds.plannedYear}
          type="number"
          min={1900}
          max={2200}
          value={fields.year}
          onChange={(e) => state.edit({ year: e.target.value })}
          onBlur={state.flush}
          className={`${input} w-24`}
        />
      </FieldRow>
      <FieldRow label="Formats" chip={chip('formats', settings.formats.inherited)}>
        {formatOptions.map((option) => (
          <label
            key={option.code}
            className="flex cursor-pointer items-center gap-1 rounded border border-border bg-surface px-2 py-1 text-sm"
          >
            <input
              type="checkbox"
              data-testid={settingsTestIds.format(option.code)}
              checked={formats.includes(option.code)}
              onChange={() => toggle(option.code)}
            />
            {option.label}
          </label>
        ))}
      </FieldRow>
      <FieldRow
        label="First print run"
        htmlFor="settings-print-run"
        chip={chip('printRun', settings.printRun.inherited)}
        help="Rights holders often price permission by print run, so say if it may grow."
      >
        <input
          id="settings-print-run"
          data-testid={settingsTestIds.printRun}
          type="number"
          min={0}
          step={500}
          value={fields.run}
          onChange={(e) => state.edit({ run: e.target.value })}
          onBlur={state.flush}
          className={`${input} w-28`}
        />
        <span className="text-sm text-muted">copies</span>
      </FieldRow>
      <FieldRow
        label="Territory and language"
        htmlFor="settings-territory"
        chip={chip('territory', settings.territory.inherited)}
      >
        <select
          id="settings-territory"
          data-testid={settingsTestIds.territory}
          value={fields.territory}
          onChange={(e) => void state.choose({ territory: e.target.value })}
          className={input}
        >
          {fields.territory === '' && <option value="">Not set</option>}
          {rights.map((right) => (
            <option key={right} value={right}>
              {right}
            </option>
          ))}
        </select>
      </FieldRow>
    </>
  );
}
