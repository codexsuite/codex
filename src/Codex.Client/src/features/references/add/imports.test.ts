import { describe, expect, it } from 'vitest';
import { parseBibtex } from './bibtexImport';
import { parseCslJson } from './cslImport';
import { detailsFromLookup, authorText } from './detailsFromLookup';
import { detectLookup } from './lookupInput';
import { parseRis } from './risImport';

describe('detectLookup', () => {
  it.each([
    ['978-0-14-044913-6', 'isbn'],
    ['ISBN 0140449132', 'isbn'],
    ['080442957X', 'isbn'],
    ['10.1000/xyz123', 'doi'],
    ['doi:10.1000/xyz123', 'doi'],
    ['https://doi.org/10.1000/xyz123', 'doi'],
    ['https://example.test/article', 'url'],
  ])('reads %s as %s', (text, kind) => {
    expect(detectLookup(text)?.kind).toBe(kind);
  });

  it.each(['', 'The Lean Startup', '12345', 'ftp://x.test'])('rejects %j', (text) => {
    expect(detectLookup(text)).toBeNull();
  });
});

describe('detailsFromLookup', () => {
  it('writes authors family first and keeps organisations', () => {
    expect(authorText('Ada Lovelace')).toBe('Lovelace, Ada');
    expect(authorText('Lovelace, Ada')).toBe('Lovelace, Ada');
    expect(authorText('Herodotus')).toBe('Herodotus');
  });

  it('fills a book from an ISBN and a paper from a DOI', () => {
    const source = {
      title: 'T',
      authors: ['Ada Lovelace'],
      year: 2019,
      publisher: 'J',
      url: 'https://doi.org/10.1/x',
    };
    const book = detailsFromLookup('isbn', '9780140449136', source);
    expect([book.kind, book.isbn, book.authorsText]).toEqual([
      'book',
      '9780140449136',
      'Lovelace, Ada',
    ]);
    const paper = detailsFromLookup('doi', '10.1000/xyz123', source);
    expect([paper.kind, paper.doi, paper.url]).toEqual([
      'paper',
      '10.1000/xyz123',
      'https://doi.org/10.1/x',
    ]);
  });
});

describe('parseBibtex', () => {
  const bib = `
@book{beck1999,
  author = {Beck, Kent and Andres, Cynthia},
  title = {Extreme Programming Explained},
  publisher = {Addison-Wesley},
  year = {1999},
  isbn = {978-0-321-27865-4}
}
@article{conway1968, author = {Melvin E. Conway}, title = {How Do Committees Invent?}, journal = {Datamation}, year = 1968, doi = {10.1/abc}}
@misc{dora, title = {DORA report}, url = {https://dora.dev/}, howpublished = {Web}}
`;

  it('maps entries to items with their keys, kinds and authors', () => {
    const { items, problems } = parseBibtex(bib);
    expect(problems).toEqual([]);
    expect(items.map((i) => [i.provider, i.key, i.details.kind, i.details.title])).toEqual([
      ['bibtex', 'beck1999', 'book', 'Extreme Programming Explained'],
      ['bibtex', 'conway1968', 'paper', 'How Do Committees Invent?'],
      ['bibtex', 'dora', 'web', 'DORA report'],
    ]);
    expect(items[0]?.details.authorsText).toBe('Beck, Kent; Andres, Cynthia');
    expect(items[0]?.details.year).toBe(1999);
    expect(items[1]?.details.authorsText).toBe('Conway, Melvin E.');
    expect(items[1]?.details.publisher).toBe('Datamation');
  });

  it('reports a broken entry instead of failing the file', () => {
    const { items, problems } = parseBibtex(
      '@book{ok, title = {Fine}}\n@book{broken, title = {Unclosed',
    );
    expect(items.map((i) => i.key)).toContain('ok');
    expect(problems.length).toBeGreaterThan(0);
  });

  it('gives nothing for text that is not BibTeX', () => {
    expect(parseBibtex('just some words').items).toEqual([]);
  });
});

describe('parseRis', () => {
  const ris = [
    'TY  - BOOK',
    'ID  - ries2011',
    'AU  - Ries, Eric',
    'TI  - The Lean Startup',
    'PY  - 2011///',
    'PB  - Crown Business',
    'SN  - 978-0-307-88789-4',
    'ER  - ',
    '',
    'TY  - JOUR',
    'AU  - Conway, Melvin E.',
    'T1  - How Do Committees',
    '      Invent?',
    'JO  - Datamation',
    'ZZ  - an unknown tag',
    'DO  - 10.1/abc',
    'ER  - ',
  ].join('\n');

  it('reads records to their ER line, continuing wrapped titles and ignoring unknown tags', () => {
    const { items, problems } = parseRis(ris);
    expect(problems).toEqual([]);
    expect(items.map((i) => [i.key, i.details.kind, i.details.title, i.details.year])).toEqual([
      ['ries2011', 'book', 'The Lean Startup', 2011],
      [null, 'paper', 'How Do Committees Invent?', null],
    ]);
    expect(items[0]?.details.isbn).toBe('978-0-307-88789-4');
    expect(items[1]?.details.doi).toBe('10.1/abc');
  });

  it('reports a record that never reaches ER and leaves it out', () => {
    const { items, problems } = parseRis(
      'TY  - BOOK\nTI  - Complete\nER  - \nTY  - BOOK\nTI  - Cut off',
    );
    expect(items.map((i) => i.details.title)).toEqual(['Complete']);
    expect(problems).toEqual(['Line 4: a record has no ER line, left out.']);
  });

  it('reports a missing ER before the next TY', () => {
    const { items, problems } = parseRis('TY  - BOOK\nTI  - One\nTY  - BOOK\nTI  - Two\nER  - ');
    expect(items.map((i) => i.details.title)).toEqual(['Two']);
    expect(problems).toEqual(['Line 1: a record has no ER line, left out.']);
  });

  it('reports lines outside any record and gives nothing for text that is not RIS', () => {
    const { items, problems } = parseRis('hello\nTI  - orphan');
    expect(items).toEqual([]);
    expect(problems).toHaveLength(2);
  });

  it('accepts Windows line endings and a byte-order mark', () => {
    const { items } = parseRis('﻿TY  - BOOK\r\nTI  - Windows\r\nER  - \r\n');
    expect(items.map((i) => i.details.title)).toEqual(['Windows']);
  });
});

describe('parseCslJson', () => {
  const library = JSON.stringify([
    {
      id: 'fowler2018',
      type: 'book',
      title: 'Refactoring',
      author: [{ family: 'Fowler', given: 'Martin' }],
      issued: { 'date-parts': [[2018, 11]] },
      publisher: 'Addison-Wesley',
      ISBN: '978-0-13-475759-9',
    },
    {
      id: 'org',
      type: 'report',
      author: [{ literal: 'ThoughtWorks' }],
      issued: { raw: '2020-03' },
    },
    { type: 'webpage', title: 'A post', URL: 'https://example.test/post', DOI: '10.1/x' },
  ]);

  it('reads authors, year, kind and identifiers and keeps the id as the key', () => {
    const { items, problems } = parseCslJson(library);
    expect(problems).toEqual([]);
    expect(items).toHaveLength(3);
    expect(items[0]).toMatchObject({
      provider: 'csl',
      key: 'fowler2018',
      details: {
        kind: 'book',
        title: 'Refactoring',
        authorsText: 'Fowler, Martin',
        year: 2018,
        publisher: 'Addison-Wesley',
        isbn: '978-0-13-475759-9',
      },
    });
    expect(items[1]?.details).toMatchObject({
      kind: 'report',
      title: '',
      authorsText: 'ThoughtWorks',
      year: 2020,
    });
    expect(items[2]).toMatchObject({ key: null, details: { kind: 'web', doi: '10.1/x' } });
  });

  it('accepts a single item and reports entries that are not objects', () => {
    expect(parseCslJson('{"title":"One"}').items).toHaveLength(1);
    expect(parseCslJson('[1, {"title":"Two"}]')).toMatchObject({
      items: [{ details: { title: 'Two' } }],
      problems: ['Entry 1: not a CSL item, left out.'],
    });
  });

  it('reports a file that is not JSON', () => {
    expect(parseCslJson('@book{x}')).toEqual({
      items: [],
      problems: [expect.stringContaining('not valid JSON')],
    });
  });
});
