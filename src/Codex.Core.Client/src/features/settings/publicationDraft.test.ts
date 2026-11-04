import { describe, expect, it } from 'vitest';
import { letterParts, parseLimit, requestOf } from './publicationDraft';

describe('parseLimit', () => {
  it('accepts whole numbers above zero only', () => {
    expect(parseLimit('250')).toBe(250);
    expect(parseLimit(' 40 ')).toBe(40);
    expect(parseLimit('0')).toBeNull();
    expect(parseLimit('')).toBeNull();
    expect(parseLimit('12.5')).toBeNull();
    expect(parseLimit('-3')).toBeNull();
  });
});

describe('requestOf', () => {
  it('sends only the edited fields and leaves invalid text out', () => {
    expect(requestOf({ limit: '200', publisher: '', year: 'abc', run: '' })).toEqual({
      quoteWordLimit: 200,
      publisher: '',
      printRun: 0,
    });
  });
  it('sends a blank year as 0 (inherit) and skips a blank territory', () => {
    expect(requestOf({ year: '', territory: ' ' })).toEqual({ plannedYear: 0 });
  });
});

describe('letterParts', () => {
  const fields = { limit: '300', publisher: '', year: '', run: '5000', territory: 'World English' };
  it('marks the missing values and words the rest', () => {
    const parts = letterParts(fields, ['print', 'ebook']);
    expect(parts.publisher.text).toBe('');
    expect(parts.formats).toEqual({ text: 'print and ebook', missing: false });
    expect(parts.year.missing).toBe(true);
    expect(parts.run.text).toBe('5,000 copies');
    expect(parts.rights.text).toBe('world English');
  });
  it('flags no format chosen', () => {
    expect(letterParts(fields, []).formats.missing).toBe(true);
  });
});
