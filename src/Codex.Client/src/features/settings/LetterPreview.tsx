import type { PublicationFormat } from '@codex/core/api/settings';
import { FieldRow } from './FieldRow';
import {
  letterParts,
  type LetterPart,
  type PublicationFields,
} from '@codex/core/features/settings/publicationDraft';
import { settingsTestIds } from './testIds';

function Mark({ part }: { part: LetterPart }) {
  return (
    <mark
      data-testid={part.missing ? settingsTestIds.letterMissing : undefined}
      className={
        part.missing
          ? 'rounded bg-warn/20 px-0.5 text-warn'
          : 'rounded bg-accent-soft px-0.5 text-text'
      }
    >
      {part.text}
    </mark>
  );
}

// The sentence every drafted permission request will use, with the values highlighted and missing ones in warn.
export function LetterPreview({
  title,
  fields,
  formats,
}: {
  title: string;
  fields: PublicationFields;
  formats: PublicationFormat[];
}) {
  const parts = letterParts(fields, formats);
  return (
    <FieldRow
      label="Letters will say"
      help="Requests already sent keep the wording they were sent with."
    >
      <p
        data-testid={settingsTestIds.letter}
        className="m-0 w-full rounded border border-border bg-bg p-2 text-sm text-text"
      >
        I am writing to ask permission to reproduce material from the source I cite in my book “
        {title}”, to be published
        {parts.publisher.text !== '' && (
          <>
            {' by '}
            <Mark part={parts.publisher} />
          </>
        )}{' '}
        in <Mark part={parts.formats} /> in <Mark part={parts.year} /> (first print run{' '}
        <Mark part={parts.run} />, <Mark part={parts.rights} /> rights).
      </p>
    </FieldRow>
  );
}
