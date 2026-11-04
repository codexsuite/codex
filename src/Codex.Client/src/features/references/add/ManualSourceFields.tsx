import type { SourceDetailsRequest } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';
import { sourceKinds } from './detailsFromLookup';

const inputClass = 'rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text';

type TextField = 'title' | 'authorsText' | 'publisher' | 'isbn' | 'doi' | 'url';

const fields: { name: TextField; label: string }[] = [
  { name: 'title', label: 'Title' },
  { name: 'authorsText', label: 'Authors (Family, Given; …)' },
  { name: 'publisher', label: 'Publisher' },
  { name: 'isbn', label: 'ISBN' },
  { name: 'doi', label: 'DOI' },
  { name: 'url', label: 'URL' },
];

// The by-hand fields of Add source: the kind, the title and the people, and whichever identifiers the writer has.
export function ManualSourceFields({
  details,
  onChange,
}: {
  details: SourceDetailsRequest;
  onChange: (details: SourceDetailsRequest) => void;
}) {
  return (
    <div className="flex flex-col gap-2">
      <label className="flex flex-col gap-1 text-xs text-muted">
        Kind
        <select
          data-testid={refsTestIds.addField('kind')}
          value={details.kind}
          onChange={(event) => onChange({ ...details, kind: event.target.value })}
          className={inputClass}
        >
          {sourceKinds.map((kind) => (
            <option key={kind.code} value={kind.code}>
              {kind.label}
            </option>
          ))}
        </select>
      </label>
      {fields.map(({ name, label }) => (
        <label key={name} className="flex flex-col gap-1 text-xs text-muted">
          {label}
          <input
            data-testid={refsTestIds.addField(name)}
            value={details[name] ?? ''}
            onChange={(event) =>
              onChange({
                ...details,
                [name]:
                  event.target.value === '' && name !== 'title' && name !== 'authorsText'
                    ? null
                    : event.target.value,
              })
            }
            spellCheck={false}
            className={inputClass}
          />
        </label>
      ))}
      <label className="flex flex-col gap-1 text-xs text-muted">
        Year
        <input
          data-testid={refsTestIds.addField('year')}
          inputMode="numeric"
          value={details.year ?? ''}
          onChange={(event) => {
            const year = Number(event.target.value);
            onChange({
              ...details,
              year: event.target.value === '' || Number.isNaN(year) ? null : year,
            });
          }}
          className={inputClass}
        />
      </label>
    </div>
  );
}
