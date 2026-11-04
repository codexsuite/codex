import type { BookScreen, SettingsSection } from './deepLink';

// Typed link builders: the one place a Codex URL is written (docs/navigation.md). Every "Open in Plan / Write /
// References", every tile and every MCP `open_url` goes through these; deepLink.ts parses what they produce.

type Pair = readonly [string, string | undefined];

const enc = encodeURIComponent;

function hashOf(bare: string | undefined, pairs: Pair[]): string {
  const parts: string[] = [];
  if (bare !== undefined) parts.push(enc(bare));
  for (const [key, value] of pairs) if (value !== undefined) parts.push(`${key}=${enc(value)}`);
  return parts.length === 0 ? '' : `#${parts.join('&')}`;
}

function queryOf(pairs: Pair[]): string {
  const parts = pairs.flatMap(([key, value]) =>
    value === undefined ? [] : [`${key}=${enc(value)}`],
  );
  return parts.length === 0 ? '' : `?${parts.join('&')}`;
}

export interface Anchor {
  /** Display number, `5.2`. */
  scene?: string;
  /** Display number, `5`. */
  chapter?: string;
  /** Highlight a Knowledge base entry. */
  kb?: string;
  /** Open the entry window. */
  entry?: string;
}

export interface Detour {
  /** Path to come back to. */
  returnTo?: string;
  sel?: string;
}

const bookPath = (bookId: string, screen: BookScreen) => `/books/${enc(bookId)}/${screen}`;

export const libraryLink = () => '/';
export const seriesLink = (seriesId: string) => `/#series=${enc(seriesId)}`;
export const newBookLink = () => '/?book=new';

export const planLink = (bookId: string, anchor: Anchor = {}) =>
  bookPath(bookId, 'plan') +
  hashOf(undefined, [
    ['scene', anchor.scene],
    ['chapter', anchor.chapter],
    ['kb', anchor.kb],
    ['entry', anchor.entry],
  ]);

export const writeLink = (bookId: string, anchor: Anchor & { ref?: string } = {}) =>
  bookPath(bookId, 'write') +
  hashOf(undefined, [
    ['scene', anchor.scene],
    ['chapter', anchor.chapter],
    ['kb', anchor.kb],
    ['entry', anchor.entry],
    ['ref', anchor.ref],
  ]);

export interface ReferencesTarget extends Detour {
  /** Select a source (`#<source>`). */
  source?: string;
  /** `#new`: start a new source. */
  isNew?: boolean;
  kb?: string;
  /** `#cite=<source>&title=<text>`: cite a source from Write. */
  cite?: string;
  title?: string;
  paraphrase?: boolean;
}

export const referencesLink = (bookId: string, target: ReferencesTarget = {}) =>
  bookPath(bookId, 'references') +
  queryOf([
    ['return', target.returnTo],
    ['sel', target.sel],
  ]) +
  hashOf(target.isNew ? 'new' : target.source, [
    ['kb', target.kb],
    ['cite', target.cite],
    ['title', target.title],
    ['paraphrase', target.paraphrase ? '1' : undefined],
  ]);

export const layoutLink = (bookId: string, anchor: Pick<Anchor, 'scene' | 'chapter'> = {}) =>
  bookPath(bookId, 'layout') +
  hashOf(undefined, [
    ['scene', anchor.scene],
    ['chapter', anchor.chapter],
  ]);

export const settingsLink = (bookId: string, section?: SettingsSection) =>
  bookPath(bookId, 'settings') + hashOf(section, []);

export function bookScreenLink(bookId: string, screen: BookScreen): string {
  return bookPath(bookId, screen);
}
