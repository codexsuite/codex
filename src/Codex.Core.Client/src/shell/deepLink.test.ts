import { describe, expect, it } from 'vitest';
import { parseDeepLink, settingsSections, type DeepLink } from './deepLink';
import {
  layoutLink,
  libraryLink,
  newBookLink,
  planLink,
  referencesLink,
  seriesLink,
  settingsLink,
  writeLink,
} from './links';

const book = '0199a000-0000-7000-8000-000000000012';

function parse(link: string) {
  const url = new URL(link, 'http://codex.test');
  return parseDeepLink(url.pathname, url.search, url.hash);
}

function parsed(link: string): DeepLink {
  const result = parse(link);
  if (!result.ok) throw new Error(result.error);
  return result.link;
}

describe('parseDeepLink', () => {
  it('DeepLink_WhenHashHasScene_ShouldParse', () => {
    expect(parsed(`/books/${book}/write#scene=5.2&kb=e:velocity&ref=velocity`)).toEqual({
      screen: 'write',
      bookId: book,
      scene: '5.2',
      kb: 'e:velocity',
      ref: 'velocity',
    });
  });

  it('DeepLink_WhenHashInvalid_ShouldReturnError', () => {
    expect(parse(`/books/${book}/plan#scene=abc`).ok).toBe(false);
    expect(parse(`/books/${book}/plan#chapter=5.2`).ok).toBe(false);
    expect(parse(`/books/${book}/plan#scene=%E0%A4%A`).ok).toBe(false);
    expect(parse(`/books/${book}/plan#scene=`).ok).toBe(false);
    expect(parse(`/books/${book}/plan#whatever`).ok).toBe(false);
    expect(parse(`/books/${book}/settings#nonsense`).ok).toBe(false);
    expect(parse(`/books/${book}/references?return=//evil.example`).ok).toBe(false);
    expect(parse(`/books/${book}/references?return=/%5Cevil.example`).ok).toBe(false);
    expect(parse(`/books/${book}/references?return=%2F%09%2Fevil.example`).ok).toBe(false);
    expect(parse(`/books/${book}/references?return=/books/x%0A/write`).ok).toBe(false);
    expect(parse(`/books/${book}/references?return=/books/x%20/write`).ok).toBe(false);
    expect(parse(`/books/${book}/frobnicate`).ok).toBe(false);
  });

  it('DeepLink_WhenBareReferencesTarget_ShouldReadSourceOrNew', () => {
    expect(parsed(`/books/${book}/references#velocity`).source).toBe('velocity');
    expect(parsed(`/books/${book}/references#new`).isNew).toBe(true);
    expect(parsed(`/books/${book}/settings#citations`).section).toBe('citations');
  });

  it('DeepLink_WhenSettingsSection_ShouldReadEveryDesignSection', () => {
    for (const section of settingsSections) {
      expect(parsed(`/books/${book}/settings#${section}`).section).toBe(section);
      expect(parsed(settingsLink(book, section)).section).toBe(section);
    }
    expect(settingsSections).toEqual([
      'book',
      'series',
      'writing',
      'citations',
      'rules',
      'letters',
      'layout',
      'sharing',
      'archive',
    ]);
    expect(parse(`/books/${book}/settings#letter`).ok).toBe(false);
  });

  it('DeepLink_WhenLibrary_ShouldReadSeriesAndNewBook', () => {
    expect(parsed('/#series=abc')).toEqual({ screen: 'library', series: 'abc' });
    expect(parsed('/?book=new')).toEqual({ screen: 'library', newBook: true });
    expect(parse('/?book=other').ok).toBe(false);
  });

  it('DeepLink_WhenUnknownKey_ShouldIgnoreIt', () => {
    expect(parsed(`/books/${book}/plan#future=1&scene=1`).scene).toBe('1');
  });
});

describe('link builders', () => {
  it('Links_WhenBuiltAndParsed_ShouldRoundTrip', () => {
    expect(parsed(libraryLink())).toEqual({ screen: 'library' });
    expect(parsed(newBookLink()).newBook).toBe(true);
    expect(parsed(seriesLink('s-1')).series).toBe('s-1');
    expect(parsed(planLink(book, { scene: '5.2', chapter: '5', kb: 'e:a', entry: 'e:b' }))).toEqual(
      {
        screen: 'plan',
        bookId: book,
        scene: '5.2',
        chapter: '5',
        kb: 'e:a',
        entry: 'e:b',
      },
    );
    expect(parsed(writeLink(book, { scene: '2.1', ref: 'velocity' })).ref).toBe('velocity');
    expect(parsed(layoutLink(book, { chapter: '3' })).chapter).toBe('3');
    expect(parsed(settingsLink(book, 'rules')).section).toBe('rules');
    expect(
      parsed(
        referencesLink(book, {
          source: 'velocity',
          returnTo: `/books/${book}/write#scene=5.2`,
          sel: 'a&b',
          cite: 'velocity',
          title: 'The Velocity Trap & more',
          paraphrase: true,
        }),
      ),
    ).toEqual({
      screen: 'references',
      bookId: book,
      source: 'velocity',
      returnTo: `/books/${book}/write#scene=5.2`,
      sel: 'a&b',
      cite: 'velocity',
      title: 'The Velocity Trap & more',
      paraphrase: true,
    });
    expect(parsed(referencesLink(book, { isNew: true })).isNew).toBe(true);
  });

  it('Links_WhenNoTarget_ShouldBeThePlainPath', () => {
    expect(planLink(book)).toBe(`/books/${book}/plan`);
    expect(settingsLink(book)).toBe(`/books/${book}/settings`);
  });
});
