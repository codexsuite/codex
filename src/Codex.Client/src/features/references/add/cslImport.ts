import type { ImportItem } from '@codex/core/api/referenceLookup';
import type { ParsedImport } from './bibtexImport';
import { blankDetails } from './detailsFromLookup';

const kindOf: Record<string, string> = {
  book: 'book',
  chapter: 'book',
  'entry-encyclopedia': 'book',
  'article-journal': 'paper',
  'article-magazine': 'paper',
  'paper-conference': 'paper',
  thesis: 'paper',
  report: 'report',
  webpage: 'web',
  'post-weblog': 'web',
  post: 'web',
  software: 'web',
  song: 'song',
  'musical-score': 'song',
};

type Json = Record<string, unknown>;

const isObject = (value: unknown): value is Json =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const text = (value: unknown): string | null => {
  if (typeof value === 'number') return String(value);
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
};

const nameOf = (value: unknown): string | null => {
  if (!isObject(value)) return null;
  const literal = text(value.literal);
  if (literal) return literal;
  const family = [text(value['non-dropping-particle']), text(value.family)]
    .filter(Boolean)
    .join(' ');
  const given = text(value.given);
  if (family === '') return given;
  return given ? `${family}, ${given}` : family;
};

const yearOf = (issued: unknown): number | null => {
  if (!isObject(issued)) return null;
  const parts = issued['date-parts'];
  if (Array.isArray(parts) && Array.isArray(parts[0])) {
    const year = Number(parts[0][0]);
    if (Number.isInteger(year)) return year;
  }
  const match = /\d{4}/.exec(text(issued.raw) ?? text(issued.literal) ?? '');
  return match ? Number(match[0]) : null;
};

// CSL keeps an ISBN as typed ("978-0-86547-xxx-x"): the server normalises it when it matches duplicates.
function itemOf(entry: Json): ImportItem {
  const url = text(entry.URL);
  const kind = kindOf[text(entry.type) ?? ''] ?? (url ? 'web' : 'book');
  return {
    provider: 'csl',
    key: text(entry.id),
    details: {
      ...blankDetails(kind),
      title: text(entry.title) ?? '',
      authorsText: (Array.isArray(entry.author) ? entry.author : [])
        .map(nameOf)
        .filter(Boolean)
        .join('; '),
      year: yearOf(entry.issued),
      publisher: text(entry.publisher) ?? text(entry['container-title']),
      edition: text(entry.edition),
      isbn: text(entry.ISBN),
      doi: text(entry.DOI),
      url,
    },
  };
}

/** True when the text is JSON that starts like a CSL JSON library, so the dialog can tell it from BibTeX and RIS. */
export const looksLikeCsl = (source: string): boolean => /^\s*[[{]/.test(source);

/**
 * Reads a CSL JSON file (what Zotero's "CSL JSON" export writes): an array of items, or one item. An entry that is
 * not an object is reported and left out; a file that is not JSON gets one problem and no items.
 */
export function parseCslJson(source: string): ParsedImport {
  let data: unknown;
  try {
    data = JSON.parse(source.replace(/^\uFEFF/, ''));
  } catch {
    return {
      items: [],
      problems: ['The file is not valid JSON, so it cannot be read as CSL JSON.'],
    };
  }
  const entries = Array.isArray(data) ? data : [data];
  const items: ImportItem[] = [];
  const problems: string[] = [];
  for (const [index, entry] of entries.entries()) {
    if (isObject(entry)) items.push(itemOf(entry));
    else problems.push(`Entry ${index + 1}: not a CSL item, left out.`);
  }
  return { items, problems };
}
