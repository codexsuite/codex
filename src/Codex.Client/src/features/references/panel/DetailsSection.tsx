import { useState } from 'react';
import type { SourceDetail, SourceDetailsRequest } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';
import { DetailField } from './DetailField';
import { columnOf, requestOf, valueOf, type FormEdits, type TextField } from './detailsForm';
import { EntryLink } from './EntryLink';
import { IdentifierBadge } from './IdentifierBadge';

const labels: Record<TextField, string> = {
  authorsText: 'Authors',
  title: 'Title',
  year: 'Year',
  publisher: 'Publisher',
  edition: 'Edition',
  isbn: 'ISBN',
  doi: 'DOI',
  url: 'URL',
  accessedOn: 'Accessed',
  rightsHolder: 'Rights holder',
};

const selectClass =
  'min-w-0 flex-1 rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text';

// The source's fields, edited in place: a field saves when it loses focus. Required fields for the kind show warn
// when empty; ISBN, DOI and URL show their last check.
export function DetailsSection({
  source,
  busy,
  onSave,
  onEntry,
  onAcceptRedirect,
  onInvalid,
}: {
  source: SourceDetail;
  busy: boolean;
  onSave: (details: SourceDetailsRequest) => Promise<boolean>;
  onEntry: (entryId: string | null) => void;
  onAcceptRedirect: (redirectTo: string) => void;
  onInvalid: (message: string) => void;
}) {
  const { details } = source;
  const [edits, setEdits] = useState<FormEdits>({});

  const save = async (next: FormEdits) => {
    const request = requestOf(details, next);
    if (request === null) {
      onInvalid('The year must be a number.');
      setEdits({});
      return;
    }
    await onSave(request);
    setEdits({});
  };
  const commit = () => {
    if (Object.keys(edits).length > 0) void save(edits);
  };
  const row = (field: TextField, mono = false) => {
    const check = details.identifiers.find(
      (identifier) => identifier.field === field.toLowerCase(),
    );
    return (
      <DetailField
        key={field}
        name={field}
        label={labels[field]}
        value={valueOf(details, edits, field)}
        missing={details.missing.includes(columnOf[field])}
        mono={mono}
        disabled={busy}
        trailing={
          check && <IdentifierBadge check={check} busy={busy} onAccept={onAcceptRedirect} />
        }
        onChange={(value) => setEdits({ ...edits, [field]: value })}
        onCommit={commit}
      />
    );
  };

  return (
    <div
      data-testid={refsTestIds.details}
      className="grid grid-cols-[6rem_1fr] items-center gap-x-2 gap-y-2"
    >
      <label htmlFor="refs-field-input-kind" className="text-xs text-muted">
        Kind
      </label>
      <select
        id="refs-field-input-kind"
        data-testid={refsTestIds.field('kind')}
        value={edits.kind ?? details.kind}
        disabled={busy}
        onChange={(event) => void save({ ...edits, kind: event.target.value })}
        className={selectClass}
      >
        {source.kinds.map((kind) => (
          <option key={kind.code} value={kind.code}>
            {kind.label}
          </option>
        ))}
      </select>
      {row('authorsText')}
      {row('title')}
      {row('year')}
      {row('publisher')}
      {(details.kind === 'book' || details.edition !== null) && row('edition')}
      {row('isbn', true)}
      {row('doi', true)}
      {row('url', true)}
      {row('accessedOn', true)}
      <label htmlFor="refs-field-input-licence" className="text-xs text-muted">
        Licence
      </label>
      <select
        id="refs-field-input-licence"
        data-testid={refsTestIds.field('licence')}
        value={edits.licence ?? details.licence}
        disabled={busy}
        onChange={(event) => void save({ ...edits, licence: event.target.value })}
        className={selectClass}
      >
        {source.licences.map((licence) => (
          <option key={licence.code} value={licence.code}>
            {licence.label}
          </option>
        ))}
      </select>
      {row('rightsHolder')}
      <span className="text-xs text-muted">Codex</span>
      <EntryLink bookId={source.bookId} entry={source.entry} busy={busy} onChange={onEntry} />
    </div>
  );
}
