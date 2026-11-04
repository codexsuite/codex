import { parse, type Creator } from '@retorquere/bibtex-parser';
import type { ImportItem } from '@codex/core/api/referenceLookup';
import { blankDetails } from './detailsFromLookup';

export interface ParsedImport {
  items: ImportItem[];
  /** Entries that could not be read, worded for the writer. */
  problems: string[];
}

const kindOf: Record<string, string> = {
  book: 'book',
  booklet: 'book',
  inbook: 'book',
  incollection: 'book',
  proceedings: 'book',
  article: 'paper',
  inproceedings: 'paper',
  conference: 'paper',
  phdthesis: 'paper',
  mastersthesis: 'paper',
  thesis: 'paper',
  techreport: 'report',
  report: 'report',
  online: 'web',
  electronic: 'web',
  www: 'web',
  software: 'web',
};

const nameOf = (creator: Creator): string => {
  if (creator.name !== undefined) return creator.name;
  const family = [creator.prefix, creator.lastName].filter(Boolean).join(' ');
  return creator.firstName ? `${family}, ${creator.firstName}` : family;
};

const yearOf = (text: string | undefined): number | null => {
  const match = /\d{4}/.exec(text ?? '');
  return match ? Number(match[0]) : null;
};

const clean = (text: string | undefined): string | null => {
  const value = text?.trim();
  return value ? value : null;
};

/** Reads a .bib file: one item per entry, the BibTeX key kept as the key. A broken entry is reported, not fatal. */
export function parseBibtex(text: string): ParsedImport {
  const library = parse(text, { sentenceCase: false });
  const items = library.entries.map((entry): ImportItem => {
    const fields = entry.fields;
    const url = clean(fields.url);
    const kind = kindOf[entry.type] ?? (url ? 'web' : 'book');
    const publisher =
      fields.publisher?.join(', ') ||
      fields.institution?.join(', ') ||
      fields.organization?.join(', ');
    return {
      provider: 'bibtex',
      key: entry.key,
      details: {
        ...blankDetails(kind),
        title: (fields.title ?? '').trim(),
        authorsText: (fields.author ?? fields.editor ?? []).map(nameOf).join('; '),
        year: yearOf(fields.year ?? fields.date),
        publisher: clean(
          publisher ??
            clean(fields.journal ?? fields.journaltitle ?? fields.howpublished) ??
            undefined,
        ),
        edition: clean(fields.edition),
        isbn: clean(fields.isbn),
        doi: clean(fields.doi),
        url,
      },
    };
  });
  return {
    items,
    problems: library.errors.map((error) => error.error),
  };
}
