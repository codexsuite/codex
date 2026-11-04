import { describe, expect, it } from 'vitest';
import { nameMatcher, type KbInfo } from './kbInfo';

const info = (id: string, name: string, aliases: string[] = []): KbInfo => ({
  id,
  name,
  category: '',
  colour: 'red',
  aliases,
});

const matcherFor = (...entries: KbInfo[]) => nameMatcher(new Map(entries.map((e) => [e.id, e])));
const found = (text: string, ...entries: KbInfo[]) =>
  matcherFor(...entries)(text).map((m) => [m.entryId, text.slice(m.from, m.to)]);

describe('nameMatcher', () => {
  it('matches whole words case-insensitively', () => {
    expect(found('goodhart met GOODHART.', info('a', 'Goodhart'))).toEqual([
      ['a', 'goodhart'],
      ['a', 'GOODHART'],
    ]);
  });

  it('matches aliases to their entry', () => {
    expect(found('The Doctor spoke.', info('a', 'Goodhart', ['The Doctor']))).toEqual([
      ['a', 'The Doctor'],
    ]);
  });

  it('matches a name followed by punctuation or a possessive', () => {
    expect(found("Goodhart's law, (Goodhart), Goodhart!", info('a', 'Goodhart'))).toHaveLength(3);
  });

  it('ignores a name that is a substring of another word', () => {
    expect(found('Goodharts and ungoodhart and Goodhart2', info('a', 'Goodhart'))).toEqual([]);
    expect(found('Annex and Joann', info('a', 'Ann'))).toEqual([]);
  });

  it('prefers the longest name at one position', () => {
    expect(found('Ann Lee came', info('a', 'Ann'), info('b', 'Ann Lee'))).toEqual([
      ['b', 'Ann Lee'],
    ]);
  });

  it('escapes regular-expression characters in names', () => {
    expect(found('see C++ now', info('a', 'C++'))).toEqual([['a', 'C++']]);
    expect(found('a.b and axb', info('a', 'a.b'))).toEqual([['a', 'a.b']]);
  });

  it('returns nothing without entries or for one-letter names', () => {
    expect(found('anything')).toEqual([]);
    expect(found('A cat', info('a', 'A'))).toEqual([]);
  });
});
