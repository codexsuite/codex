import type { SourceDetails, SourceDetailsRequest } from '@codex/core/api/referenceSources';

// The text fields of the details form, by the name the form and its test ids use.
export const textFields = [
  'authorsText',
  'title',
  'year',
  'publisher',
  'edition',
  'isbn',
  'doi',
  'url',
  'accessedOn',
  'rightsHolder',
] as const;

export type TextField = (typeof textFields)[number];
export type FormEdits = Partial<Record<TextField | 'kind' | 'licence', string>>;

// The reference column each form field is checked against in `missing`.
export const columnOf: Record<TextField, string> = {
  authorsText: 'authors',
  title: 'title',
  year: 'year',
  publisher: 'publisher',
  edition: 'edition',
  isbn: 'isbn',
  doi: 'doi',
  url: 'url',
  accessedOn: 'accessed_on',
  rightsHolder: 'rights_holder',
};

export function valueOf(details: SourceDetails, edits: FormEdits, field: TextField): string {
  const edited = edits[field];
  if (edited !== undefined) return edited;
  const stored = details[field];
  return stored === null ? '' : String(stored);
}

// The request that saves the form: the server's values with the edits laid over them. Null when the year is not a number.
export function requestOf(details: SourceDetails, edits: FormEdits): SourceDetailsRequest | null {
  const text = (field: TextField) => valueOf(details, edits, field);
  const year = text('year').trim();
  if (year !== '' && !/^\d{1,4}$/.test(year)) return null;
  return {
    kind: edits.kind ?? details.kind,
    title: text('title'),
    authorsText: text('authorsText'),
    year: year === '' ? null : Number(year),
    publisher: text('publisher'),
    edition: text('edition'),
    isbn: text('isbn'),
    doi: text('doi'),
    url: text('url'),
    accessedOn: text('accessedOn'),
    licence: edits.licence ?? details.licence,
    rightsHolder: text('rightsHolder'),
  };
}
