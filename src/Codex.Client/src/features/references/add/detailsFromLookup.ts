import type { LookupKind, SourceMetadata } from '@codex/core/api/referenceLookup';
import type { SourceDetailsRequest } from '@codex/core/api/referenceSources';

export const sourceKinds = [
  { code: 'book', label: 'Book' },
  { code: 'paper', label: 'Paper' },
  { code: 'web', label: 'Web page' },
  { code: 'report', label: 'Report' },
  { code: 'song', label: 'Song' },
] as const;

const kindOf: Record<LookupKind, string> = { isbn: 'book', doi: 'paper', url: 'web' };

/** "Ada Lovelace" becomes "Lovelace, Ada"; a name that already has a comma, or is one word, stays as it is. */
export function authorText(name: string): string {
  const trimmed = name.trim();
  if (trimmed.includes(',')) return trimmed;
  const words = trimmed.split(/\s+/);
  if (words.length < 2) return trimmed;
  return `${words[words.length - 1]}, ${words.slice(0, -1).join(' ')}`;
}

export const blankDetails = (kind = 'book'): SourceDetailsRequest => ({
  kind,
  title: '',
  authorsText: '',
  year: null,
  publisher: null,
  edition: null,
  isbn: null,
  doi: null,
  url: null,
  accessedOn: null,
  licence: 'unknown',
  rightsHolder: null,
});

/** The fields a look-up fills in; `value` is the canonical identifier the server returned. */
export function detailsFromLookup(
  kind: LookupKind,
  value: string,
  source: SourceMetadata,
): SourceDetailsRequest {
  return {
    ...blankDetails(kindOf[kind]),
    title: source.title,
    authorsText: source.authors.map(authorText).join('; '),
    year: source.year,
    publisher: source.publisher,
    isbn: kind === 'isbn' ? value : null,
    doi: kind === 'doi' ? value : null,
    url: kind === 'url' ? (source.url ?? value) : kind === 'doi' ? source.url : null,
  };
}
