import type { Page } from 'playwright';
import type { SourceCsl, SourceDetail, SourceRow } from '@codex/core/api/referenceSources';
import type { QuoteFlag } from '@codex/core/api/quoteFlags';
import type { ClearanceSummary } from '@codex/core/api/references';

const source = (
  id: string,
  number: number | null,
  title: string,
  status: string,
  extra: Partial<SourceRow> = {},
): SourceRow => ({
  id,
  number,
  title,
  authors: 'Beck',
  year: 1999,
  publisher: 'Addison-Wesley',
  kind: 'book',
  kindLabel: 'Book',
  licenceLabel: 'All rights reserved',
  status,
  reason:
    status === 'cleared'
      ? 'signed_off'
      : status === 'not_cited'
        ? 'no_markers'
        : 'ready_to_sign_off',
  uses: number === null ? 0 : 1,
  usedIn: number === null ? '' : 'Ch 2',
  quotedWords: 0,
  limit: 300,
  cited: number !== null,
  clearedLabel: status === 'cleared' ? 'Cleared by JD · 9 Oct' : null,
  reminderLabel: null,
  reminderDue: false,
  ...extra,
});

export const stubSources: SourceRow[] = [
  source('aaaaaaaa-0000-4000-8000-000000000001', 1, 'Extreme Programming Explained', 'cleared'),
  source('aaaaaaaa-0000-4000-8000-000000000002', 2, 'The Mythical Man-Month', 'needs_check', {
    authors: 'Brooks',
    year: 1975,
    kindLabel: 'Book',
  }),
  source('aaaaaaaa-0000-4000-8000-000000000003', 3, 'The Velocity Trap', 'risk', {
    authors: 'Whitlock',
    year: 2021,
    reason: 'quote_over_limit',
    quotedWords: 342,
    uses: 2,
    usedIn: 'Ch 4, 8',
  }),
  source(
    'aaaaaaaa-0000-4000-8000-000000000004',
    4,
    'Manifesto for Agile Software Development',
    'needs_check',
    {
      authors: 'Beck et al.',
      year: 2001,
      kind: 'web',
      kindLabel: 'Web page',
    },
  ),
  source('aaaaaaaa-0000-4000-8000-000000000005', null, 'Lean Software Development', 'not_cited', {
    authors: 'Poppendieck',
  }),
];

export const stubSummary: ClearanceSummary = {
  cleared: 1,
  total: 5,
  risks: 1,
  needsCheck: 2,
  notCited: 1,
  badge: 1,
  remindersDue: 0,
};

const cslOf = (row: SourceRow, family: string): SourceCsl => ({
  sourceId: row.id,
  status: row.status,
  csl: {
    id: row.id,
    type: 'book',
    title: row.title,
    author: [{ family, given: 'Pat' }],
    issued: { 'date-parts': [[row.year ?? 2000]] },
    publisher: row.publisher ?? 'Press',
  },
  creditLine: row.status === 'cleared' ? 'Reprinted with permission of Addison-Wesley.' : null,
});

export const stubCsl: SourceCsl[] = [
  cslOf(stubSources[0], 'Beck'),
  cslOf(stubSources[1], 'Brooks'),
  cslOf(stubSources[2], 'Whitlock'),
  cslOf(stubSources[3], 'Beck'),
];

/** Serves the References endpoints and the saved citation style for one book. Component tests only. */
export async function stubReferencesApi(page: Page, bookId: string, style = 'chicago') {
  const base = `**/api/books/${bookId}`;
  await page.route(`${base}/sources`, (route) => route.fulfill({ json: stubSources }));
  await page.route(`${base}/sources/csl`, (route) => route.fulfill({ json: stubCsl }));
  await page.route(`${base}/publication-settings`, (route) =>
    route.fulfill({
      json: { bookId, style: { value: style, inherited: false }, rulesName: 'Default' },
    }),
  );
  await page.route(`${base}/references/summary`, (route) => route.fulfill({ json: stubSummary }));
  await page.route(`**/api/references/*`, (route) => {
    const id = route.request().url().split('/').pop() ?? '';
    const row = stubSources.find((candidate) => candidate.id === id);
    if (route.request().method() !== 'GET' || row === undefined) return route.fallback();
    return route.fulfill({ json: stubDetail(row, { bookId }) });
  });
}

/** A source detail panel body for one table row. Override fields per test. */
export function stubDetail(row: SourceRow, extra: Partial<SourceDetail> = {}): SourceDetail {
  return {
    id: row.id,
    bookId: 'book',
    number: row.number,
    kind: row.kind,
    kindLabel: row.kindLabel,
    title: row.title,
    authorsLabel: row.authors,
    year: row.year,
    publisher: row.publisher,
    verdict: {
      status: row.status,
      reason: row.reason,
      action:
        row.status === 'cleared' ? 'reopen' : row.status === 'risk' ? 'permission' : 'sign_off',
      signedOffBy: row.status === 'cleared' ? 'Joel Dickson' : null,
      signedOffLabel: row.status === 'cleared' ? '8d ago' : null,
    },
    checks: [
      {
        key: 'words',
        status: 'ok',
        title: 'Prose quoted against the limit',
        detail: 'No prose quoted.',
      },
      { key: 'licence', status: 'ok', title: 'Licence', detail: row.licenceLabel },
    ],
    uses: [
      {
        id: 'bbbbbbbb-0000-4000-8000-000000000001',
        sceneId: 'cccccccc-0000-4000-8000-000000000001',
        chapterNumber: 4,
        chapterTitle: 'The metrics trap',
        sceneNumber: 1,
        kind: 'quote',
        excerpt: 'Adding manpower to a late software project makes it later.',
        words: 10,
        locator: 'p. 25',
        newSinceSignOff: false,
        pending: false,
      },
    ],
    quotedWords: row.quotedWords,
    limit: row.limit,
    entry: null,
    details: {
      kind: row.kind,
      title: row.title,
      authorsText: 'Beck, Kent',
      year: row.year,
      publisher: row.publisher,
      edition: null,
      isbn: null,
      doi: null,
      url: null,
      accessedOn: null,
      licence: 'all_rights_reserved',
      rightsHolder: null,
      fairUseNote: null,
      missing: [],
      identifiers: [],
    },
    kinds: [
      { code: 'book', label: 'Book' },
      { code: 'web', label: 'Web page' },
    ],
    licences: [
      { code: 'all_rights_reserved', label: 'All rights reserved' },
      { code: 'public_domain', label: 'Public domain' },
    ],
    history: [{ when: '2h ago', who: 'Joel Dickson', text: 'marked the source as cleared' }],
    permission: null,
    ...extra,
  };
}

/** Serves GET /api/references/<id> for one source, from a body the test can read and replace. */
export async function stubSourceDetail(page: Page, detail: SourceDetail) {
  await page.route(`**/api/references/${detail.id}`, (route) => {
    if (route.request().method() === 'GET') return route.fulfill({ json: detail });
    return route.fallback();
  });
}

export const stubQuoteFlags: QuoteFlag[] = [
  {
    id: 'bbbbbbbb-0000-4000-8000-000000000001',
    sceneId: 'cccccccc-0000-4000-8000-000000000001',
    chapterNumber: 6,
    chapterTitle: 'Listening to users',
    sceneNumber: 1,
    reason: 'quotation_marks',
    why: 'In quotation marks, with no source marker',
    excerpt: 'The customer is not in the building',
    referenceId: null,
    referenceLabel: null,
    matchPercent: null,
    locator: null,
  },
  {
    id: 'bbbbbbbb-0000-4000-8000-000000000002',
    sceneId: 'cccccccc-0000-4000-8000-000000000002',
    chapterNumber: 8,
    chapterTitle: 'What we did instead',
    sceneNumber: 1,
    reason: 'source_match',
    why: '93% match with Whitlock, 2021, p. 112',
    excerpt: 'Velocity measures activity, not progress.',
    referenceId: 'aaaaaaaa-0000-4000-8000-000000000003',
    referenceLabel: 'Whitlock, 2021',
    matchPercent: 93,
    locator: 'p. 112',
  },
];

/** Serves a book's unflagged-quotes inbox: the list, and each resolution answering with the flags left. Component tests only. */
export async function stubQuoteFlagsApi(page: Page, bookId: string, flags = stubQuoteFlags) {
  let open = flags;
  await page.route(`**/api/books/${bookId}/quote-flags`, (route) => route.fulfill({ json: open }));
  await page.route('**/api/quote-flags/*/*', (route) => {
    const flagId = route.request().url().split('/quote-flags/')[1].split('/')[0];
    open = open.filter((flag) => flag.id !== flagId);
    return route.fulfill({ json: open });
  });
}
