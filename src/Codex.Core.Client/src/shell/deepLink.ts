// The parsed form of a Codex URL: path, `?query` and `#hash` (design/README.md -> Moving between screens).
// Pure: no React, no window. links.ts builds the strings this parses; the two are round-trip tested.

export const bookScreens = ['plan', 'write', 'references', 'layout', 'settings'] as const;
export type BookScreen = (typeof bookScreens)[number];

// design/README.md -> Book settings patterns: one per section card, plus #rules and #letters inside Citations.
export const settingsSections = [
  'book',
  'series',
  'writing',
  'citations',
  'rules',
  'letters',
  'layout',
  'sharing',
  'archive',
] as const;
export type SettingsSection = (typeof settingsSections)[number];

export interface DeepLink {
  screen: 'library' | BookScreen;
  bookId?: string;
  /** Library `#series=<id>`: the Series screen. */
  series?: string;
  /** Library `?book=new`: the New book dialog. */
  newBook?: boolean;
  /** `#scene=5.2`, a display number. */
  scene?: string;
  /** `#chapter=5`, a display number. */
  chapter?: string;
  /** `#kb=<entry>`: highlight a Knowledge base entry. */
  kb?: string;
  /** `#entry=<entry>`: open the entry window. */
  entry?: string;
  /** Write `&ref=<source>`: open that marker's card. */
  ref?: string;
  /** References bare `#<source>`: select a source. */
  source?: string;
  /** References bare `#new`. */
  isNew?: boolean;
  /** Settings bare `#book`, `#series`, `#writing`, `#citations`, `#rules`, `#letters`, `#layout` or `#archive`. */
  section?: SettingsSection;
  /** References `#cite=<source>&title=<text>`. */
  cite?: string;
  title?: string;
  /** References `&paraphrase=1`. */
  paraphrase?: boolean;
  /** `?return=<path>&sel=<selection>`: where a detour comes back to. */
  returnTo?: string;
  sel?: string;
}

export type ParsedDeepLink = { ok: true; link: DeepLink } | { ok: false; error: string };

const idPattern = /^[\w.:-]+$/;
const displayNumber = /^\d+(\.\d+)?$/;
const chapterNumber = /^\d+$/;

const fail = (error: string): ParsedDeepLink => ({ ok: false, error });

function decode(raw: string): string | null {
  try {
    return decodeURIComponent(raw);
  } catch {
    return null;
  }
}

interface Tokens {
  pairs: Map<string, string>;
  bare: string[];
}

function tokenize(source: string): Tokens | string {
  const pairs = new Map<string, string>();
  const bare: string[] = [];
  for (const token of source.split('&')) {
    if (token === '') continue;
    const eq = token.indexOf('=');
    const key = decode(eq < 0 ? token : token.slice(0, eq));
    const value = eq < 0 ? '' : decode(token.slice(eq + 1));
    if (key === null || value === null) return `"${token}" is not valid URL text.`;
    if (eq < 0) bare.push(key);
    else if (value === '') return `"${key}" has no value.`;
    else pairs.set(key, value);
  }
  return { pairs, bare };
}

function parsePath(pathname: string): { screen: DeepLink['screen']; bookId?: string } | string {
  if (pathname === '/' || pathname === '') return { screen: 'library' };
  const match = /^\/books\/([^/]+)\/([^/]+)\/?$/.exec(pathname);
  if (!match) return `"${pathname}" is not a Codex screen.`;
  const bookId = decode(match[1]);
  const screen = bookScreens.find((s) => s === match[2]);
  if (bookId === null || screen === undefined) return `"${pathname}" is not a Codex screen.`;
  return { screen, bookId };
}

type Setter = (link: DeepLink, value: string) => void;

// value rule per key; a rule returns an error text or null
const keyRules: Record<string, { check: (v: string) => string | null; set: Setter }> = {
  scene: {
    check: (v) => (displayNumber.test(v) ? null : `Scene "${v}" is not a number such as 5.2.`),
    set: (l, v) => (l.scene = v),
  },
  chapter: {
    check: (v) => (chapterNumber.test(v) ? null : `Chapter "${v}" is not a number such as 5.`),
    set: (l, v) => (l.chapter = v),
  },
  kb: { check: idCheck('kb'), set: (l, v) => (l.kb = v) },
  entry: { check: idCheck('entry'), set: (l, v) => (l.entry = v) },
  ref: { check: idCheck('ref'), set: (l, v) => (l.ref = v) },
  cite: { check: idCheck('cite'), set: (l, v) => (l.cite = v) },
  title: { check: () => null, set: (l, v) => (l.title = v) },
  paraphrase: {
    check: (v) => (v === '1' ? null : `paraphrase must be 1, not "${v}".`),
    set: (l) => (l.paraphrase = true),
  },
};

function idCheck(key: string) {
  return (v: string) => (idPattern.test(v) ? null : `${key} "${v}" is not a valid id.`);
}

function applyHash(link: DeepLink, hash: string): string | null {
  const tokens = tokenize(hash.replace(/^#/, ''));
  if (typeof tokens === 'string') return tokens;
  for (const [key, value] of tokens.pairs) {
    if (key === 'series') {
      if (link.screen !== 'library') return 'series only belongs on the Library.';
      if (!idPattern.test(value)) return `series "${value}" is not a valid id.`;
      link.series = value;
      continue;
    }
    const rule = keyRules[key];
    if (rule === undefined) continue; // other lanes' keys are theirs to read; not an error here
    const problem = rule.check(value);
    if (problem !== null) return problem;
    rule.set(link, value);
  }
  for (const word of tokens.bare) {
    const problem = applyBare(link, word);
    if (problem !== null) return problem;
  }
  return null;
}

function applyBare(link: DeepLink, word: string): string | null {
  if (link.screen === 'references') {
    if (word === 'new') link.isNew = true;
    else if (idPattern.test(word)) link.source = word;
    else return `"${word}" is not a source id.`;
    return null;
  }
  if (link.screen === 'settings') {
    const section = settingsSections.find((s) => s === word);
    if (section === undefined) return `"${word}" is not a settings section.`;
    link.section = section;
    return null;
  }
  return `"${word}" does not mean anything on this screen.`;
}

function applyQuery(link: DeepLink, search: string): string | null {
  const tokens = tokenize(search.replace(/^\?/, ''));
  if (typeof tokens === 'string') return tokens;
  const returnTo = tokens.pairs.get('return');
  if (returnTo !== undefined) {
    // A backslash, a control character or whitespace can turn a path into another origin (`/\evil.example`).
    if (!returnTo.startsWith('/') || returnTo.startsWith('//') || /[\\\s\p{Cc}]/u.test(returnTo)) {
      return `return "${returnTo}" must be a path inside Codex.`;
    }
    link.returnTo = returnTo;
  }
  const sel = tokens.pairs.get('sel');
  if (sel !== undefined) link.sel = sel;
  const book = tokens.pairs.get('book');
  if (book !== undefined) {
    if (link.screen !== 'library' || book !== 'new') return `book "${book}" is not a Codex link.`;
    link.newBook = true;
  }
  return null;
}

export function parseDeepLink(pathname: string, search: string, hash: string): ParsedDeepLink {
  const path = parsePath(pathname);
  if (typeof path === 'string') return fail(path);
  const link: DeepLink = { ...path };
  const problem = applyQuery(link, search) ?? applyHash(link, hash);
  return problem === null ? { ok: true, link } : fail(problem);
}
