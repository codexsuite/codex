import type { ImportItem } from '@codex/core/api/referenceLookup';
import type { ParsedImport } from './bibtexImport';
import { blankDetails } from './detailsFromLookup';

const kindOf: Record<string, string> = {
  BOOK: 'book',
  CHAP: 'book',
  EBOOK: 'book',
  EDBOOK: 'book',
  JOUR: 'paper',
  CONF: 'paper',
  CPAPER: 'paper',
  THES: 'paper',
  RPRT: 'report',
  ELEC: 'web',
  WEB: 'web',
  BLOG: 'web',
  ICOMM: 'web',
  SOUND: 'song',
  MUSIC: 'song',
};

type Record_ = Map<string, string[]>;

// "XX  - value": a two-character tag, two spaces, a dash. A line without one continues the previous value.
const lineOf = /^([A-Z][A-Z0-9]) {2}- ?(.*)$/;

const first = (record: Record_, ...tags: string[]): string | null => {
  for (const tag of tags) {
    const value = record.get(tag)?.[0]?.trim();
    if (value) return value;
  }
  return null;
};

// SN holds an ISBN or an ISSN: keep it only when it looks like an ISBN.
const isbnOf = (text: string | null): string | null =>
  text !== null && /^[\dXx\s-]{10,17}$/.test(text) ? text : null;

function itemOf(record: Record_): ImportItem {
  const kind = kindOf[first(record, 'TY') ?? ''] ?? (first(record, 'UR') ? 'web' : 'book');
  const year = /\d{4}/.exec(first(record, 'PY', 'Y1', 'DA') ?? '');
  const authors = [...(record.get('AU') ?? []), ...(record.get('A1') ?? [])]
    .map((n) => n.trim())
    .filter(Boolean);
  return {
    provider: 'ris',
    key: first(record, 'ID'),
    details: {
      ...blankDetails(kind),
      title: first(record, 'TI', 'T1') ?? '',
      authorsText: authors.join('; '),
      year: year ? Number(year[0]) : null,
      publisher: first(record, 'PB', 'JO', 'JF', 'T2'),
      edition: first(record, 'ET'),
      isbn: isbnOf(first(record, 'SN')),
      doi: first(record, 'DO'),
      url: first(record, 'UR', 'L1'),
    },
  };
}

/**
 * Reads an .ris file by hand. A record runs to `ER  -`; tags this reader does not know are ignored. A record that
 * never reaches `ER` (a cut-off file) is reported and left out; text before the first tag is reported.
 */
export function parseRis(text: string): ParsedImport {
  const items: ImportItem[] = [];
  const problems: string[] = [];
  let record: Record_ | null = null;
  let lastTag = '';
  let startLine = 0;

  const lines = text.split(/\r?\n/);
  for (const [index, raw] of lines.entries()) {
    const line = raw.replace(/^\uFEFF/, '');
    if (line.trim() === '') continue;
    const match = lineOf.exec(line);
    if (match === null) {
      if (record !== null && lastTag !== '') {
        const values = record.get(lastTag) ?? [];
        values[values.length - 1] = `${values[values.length - 1] ?? ''} ${line.trim()}`;
      } else {
        problems.push(`Line ${index + 1}: not an RIS line, skipped.`);
      }
      continue;
    }
    const [, tag, value] = match as unknown as [string, string, string];
    if (tag === 'ER') {
      if (record !== null) items.push(itemOf(record));
      record = null;
      lastTag = '';
      continue;
    }
    if (tag === 'TY') {
      if (record !== null) problems.push(`Line ${startLine}: a record has no ER line, left out.`);
      record = new Map();
      startLine = index + 1;
    }
    if (record === null) {
      problems.push(`Line ${index + 1}: ${tag} before any TY line, skipped.`);
      continue;
    }
    record.set(tag, [...(record.get(tag) ?? []), value]);
    lastTag = tag;
  }
  if (record !== null) problems.push(`Line ${startLine}: a record has no ER line, left out.`);
  return { items, problems };
}
