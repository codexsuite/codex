import { citationStyles, formatOptions } from '@codex/core/features/settings/publicationDraft';
import type { CitationStyle, PublicationFormat } from '@codex/core/api/settings';

export const statusOptions = [
  { value: 'planning', label: 'Planning' },
  { value: 'draft', label: 'Draft' },
  { value: 'writing', label: 'Writing' },
  { value: 'revising', label: 'Revising' },
  { value: 'published', label: 'Published' },
] as const;

export const statusLabel = (status: string): string =>
  statusOptions.find((o) => o.value === status)?.label ?? status;

export const citationStyleLabel = (style: CitationStyle): string =>
  citationStyles.find((s) => s.code === style)?.label ?? style;

export const NOT_SET = 'Not set';

export const countText = (value: number | null): string =>
  value === null ? NOT_SET : value.toLocaleString('en-US');

export const quoteLimitText = (limit: number): string =>
  `${limit.toLocaleString('en-US')} words prose`;

/** "" means the author publishes it themselves; null means nothing was entered. */
export function publisherText(publisher: string | null): string {
  if (publisher === null) return NOT_SET;
  return publisher === '' ? 'Self-published' : publisher;
}

export function formatsText(formats: PublicationFormat[]): string {
  const labels = formatOptions.filter((o) => formats.includes(o.code)).map((o) => o.label);
  return labels.length === 0 ? NOT_SET : labels.join(', ');
}

export const textOrNotSet = (value: string | null): string =>
  value === null || value.trim() === '' ? NOT_SET : value;

/** The start of a long text for a row's second line: whitespace collapsed, cut on a word with an ellipsis. */
export function preview(text: string | null, max = 90): string | undefined {
  const flat = (text ?? '').replace(/\s+/g, ' ').trim();
  if (flat === '') return undefined;
  if (flat.length <= max) return flat;
  const cut = flat.slice(0, max);
  const space = cut.lastIndexOf(' ');
  return `${(space > max / 2 ? cut.slice(0, space) : cut).trimEnd()}…`;
}

export type NumberParse = { ok: true; value: number } | { ok: false; message: string };

/** A whole number typed into a field. Blank is 0 (the server clears the value) when `allowBlank`. */
export function parseWholeNumber(
  text: string,
  { allowBlank, min = 0 }: { allowBlank: boolean; min?: number },
): NumberParse {
  const trimmed = text.trim().replace(/,/g, '');
  if (trimmed === '') {
    return allowBlank ? { ok: true, value: 0 } : { ok: false, message: 'Enter a whole number.' };
  }
  if (!/^\d+$/.test(trimmed)) return { ok: false, message: 'Use digits only.' };
  const value = Number.parseInt(trimmed, 10);
  return value < min
    ? { ok: false, message: `Enter a whole number of ${min} or more.` }
    : { ok: true, value };
}
