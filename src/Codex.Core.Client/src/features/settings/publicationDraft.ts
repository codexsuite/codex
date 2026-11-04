import type {
  PatchPublicationSettingsRequest,
  PublicationFormat,
  PublicationSettings,
} from '../../api/settings';

export const citationStyles = [
  { code: 'chicago', label: 'Chicago' },
  { code: 'apa', label: 'APA' },
  { code: 'harvard', label: 'Harvard' },
] as const;

export const formatOptions: { code: PublicationFormat; label: string }[] = [
  { code: 'print', label: 'Print' },
  { code: 'ebook', label: 'Ebook' },
  { code: 'audio', label: 'Audiobook' },
  { code: 'pdf', label: 'PDF' },
];

export const territories = [
  'World English',
  'World, all languages',
  'North America',
  'UK and Commonwealth',
  'Australia and New Zealand',
];

export const defaultLimit = 300;

/** The typed fields; each stays text while the reader types and is only an entry once it differs from the server. */
export interface PublicationEdits {
  limit?: string;
  publisher?: string;
  year?: string;
  run?: string;
  territory?: string;
}

export interface PublicationFields {
  limit: string;
  publisher: string;
  year: string;
  run: string;
  territory: string;
}

export const fieldsOf = (settings: PublicationSettings): PublicationFields => ({
  limit: String(settings.quoteWordLimit.value),
  publisher: settings.publisher.value ?? '',
  year: settings.plannedYear.value === null ? '' : String(settings.plannedYear.value),
  run: settings.printRun.value === null ? '' : String(settings.printRun.value),
  territory: settings.territory.value ?? '',
});

/** The limit as a whole number above 0, or null while the text isn't one. */
export function parseLimit(text: string): number | null {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const value = Number.parseInt(trimmed, 10);
  return value > 0 ? value : null;
}

/** A year or print run: blank is 0 (back to inheriting); text that isn't a whole number is not sent. */
function parseCount(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === '') return 0;
  return /^\d+$/.test(trimmed) ? Number.parseInt(trimmed, 10) : null;
}

/** What to PATCH for these edits. Invalid text is left out (the field shows its error instead). */
export function requestOf(edits: PublicationEdits): PatchPublicationSettingsRequest {
  const request: PatchPublicationSettingsRequest = {};
  if (edits.limit !== undefined) {
    const limit = parseLimit(edits.limit);
    if (limit !== null) request.quoteWordLimit = limit;
  }
  if (edits.publisher !== undefined) request.publisher = edits.publisher;
  if (edits.year !== undefined) {
    const year = parseCount(edits.year);
    if (year !== null) request.plannedYear = year;
  }
  if (edits.run !== undefined) {
    const run = parseCount(edits.run);
    if (run !== null) request.printRun = run;
  }
  if (edits.territory !== undefined && edits.territory.trim() !== '')
    request.territory = edits.territory;
  return request;
}

export const hasRequest = (request: PatchPublicationSettingsRequest) =>
  Object.keys(request).length > 0;

export interface LetterPart {
  text: string;
  /** A value the letter needs but the book doesn't have yet. */
  missing: boolean;
}

const joinFormats = (labels: string[]) =>
  labels.length > 1
    ? `${labels.slice(0, -1).join(', ')} and ${labels[labels.length - 1]}`
    : labels[0];

/** The values the letter sentence fills in, in reading order: publisher, formats, year, print run, rights. */
export function letterParts(
  fields: PublicationFields,
  formats: PublicationFormat[],
): Record<'publisher' | 'formats' | 'year' | 'run' | 'rights', LetterPart> {
  const labels = formatOptions
    .filter((o) => formats.includes(o.code))
    .map((o) => o.label.toLowerCase());
  const run = parseCount(fields.run);
  return {
    publisher: { text: fields.publisher.trim(), missing: false },
    formats: labels.length
      ? { text: joinFormats(labels), missing: false }
      : { text: 'no format chosen', missing: true },
    year: fields.year.trim()
      ? { text: fields.year.trim(), missing: false }
      : { text: 'year', missing: true },
    run: run
      ? { text: `${run.toLocaleString('en-US')} copies`, missing: false }
      : { text: 'print run', missing: true },
    rights: fields.territory.trim()
      ? { text: fields.territory.trim().replace(/^World(?=[ ,])/, 'world'), missing: false }
      : { text: 'territory', missing: true },
  };
}
