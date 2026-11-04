import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type {
  BookSettings,
  PublishState,
  PublicationSettings,
  SeriesMovePreview,
  SeriesNumberChange,
} from '@codex/core/api/settings';
import { editingTestIds } from '../../editing/testIds';
import { AppRoutes } from '../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { shellTestIds } from '../../shell/shellTestIds';
import { settingsTestIds as ids } from './testIds';

const book = '0199a000-0000-7000-8000-000000000012';
const series = '0199a000-0000-7000-8000-0000000000a1';
const other = '0199a000-0000-7000-8000-0000000000a2';
const first = '0199a000-0000-7000-8000-000000000011';
const third = '0199a000-0000-7000-8000-000000000013';

const settings: BookSettings = {
  id: book,
  title: 'Hymns for Machines',
  authorName: 'Joel Dickson',
  status: 'writing',
  targetWords: 40000,
  wordCount: 10000,
  progressPercent: 25,
  seriesId: series,
  seriesName: 'Hymn Cycle',
  seriesNumber: 2,
  seriesOptions: [
    { id: series, name: 'Hymn Cycle', bookCount: 3 },
    { id: other, name: 'Other Cycle', bookCount: 1 },
  ],
  seriesOrder: [
    { id: first, title: 'First Hymn', number: 1, status: 'published' },
    { id: book, title: 'Hymns for Machines', number: 2, status: 'writing' },
    { id: third, title: 'Third Hymn', number: 3, status: 'planning' },
  ],
  styleGuide: 'Close third.',
  seriesGuide: 'Series voice: dry.',
  archived: false,
  subtitle: null,
  description: null,
};

const publication: PublicationSettings = {
  bookId: book,
  seriesName: 'Hymn Cycle',
  style: { value: 'chicago', inherited: true },
  rulesName: 'Default',
  quoteWordLimit: { value: 300, inherited: true },
  publisher: { value: null, inherited: true },
  plannedYear: { value: null, inherited: true },
  formats: { value: [], inherited: true },
  printRun: { value: null, inherited: true },
  territory: { value: null, inherited: true },
};

interface Calls {
  patches: unknown[];
  numbers: unknown[];
  moves: unknown[];
}

async function stubSettings(page: Page, initial: BookSettings = settings): Promise<Calls> {
  const calls: Calls = { patches: [], numbers: [], moves: [] };
  let current = initial;
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book}/settings`, async (route) => {
    if (route.request().method() === 'PATCH') {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      calls.patches.push(body);
      current = {
        ...current,
        ...(typeof body.title === 'string' ? { title: body.title } : {}),
        ...(typeof body.status === 'string' ? { status: body.status } : {}),
      };
    }
    return route.fulfill({ json: current });
  });
  await page.route(`**/api/books/${book}/publication-settings`, (route) =>
    route.fulfill({ json: publication }),
  );
  await page.route(`**/api/books/${book}/settings/rules-impact**`, (route) =>
    route.fulfill({ json: { limit: 300, riskCount: 0, sources: [] } }),
  );
  await page.route(`**/api/books/${book}/settings/layout`, (route) =>
    route.fulfill({ json: { template: null, riskCount: 0 } }),
  );
  await page.route(`**/api/books/${book}/series-number`, async (route) => {
    const body = route.request().postDataJSON() as { number: number };
    calls.numbers.push(body);
    const change: SeriesNumberChange = {
      bookId: book,
      seriesId: series,
      number: body.number,
      previousNumber: current.seriesNumber ?? 0,
      books: [
        { id: book, number: body.number, previousNumber: current.seriesNumber ?? 0 },
        { id: first, number: body.number === 1 ? 2 : 1, previousNumber: 1 },
      ],
    };
    current = { ...current, seriesNumber: body.number };
    return route.fulfill({ json: change });
  });
  return calls;
}

const open = (mount: Parameters<Parameters<typeof test>[2]>[0]['mount'], hash = '') =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book}/settings${hash}` } });

test('Settings_WhenOpened_ShouldShowBookSeriesAndWriting', async ({ mount, page }) => {
  await stubSettings(page);
  await open(mount);

  await expect(page.getByTestId(ids.title)).toHaveValue('Hymns for Machines');
  await expect(page.getByTestId(ids.progressText)).toHaveText('10,000 of 40,000 · 25%');
  await expect(page.getByTestId(ids.statusOption('writing'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByTestId(ids.numberLine)).toHaveText('Book 002 · before Third Hymn');
  await expect(page.getByTestId(ids.seriesGuide)).toContainText('Series voice: dry.');
  await expect(page.getByTestId(ids.guideWords)).toHaveText('2 words');
});

test('Settings_WhenOpened_ShouldShowTheCoverRowAndTheAutosaveNote', async ({ mount, page }) => {
  await stubSettings(page);
  await open(mount);

  await expect(page.getByTestId(ids.cover)).toBeVisible();
  // The book has no uploaded cover: Replace… can add one, Remove has nothing to remove.
  await expect(page.getByTestId(ids.cover)).not.toHaveAttribute('src');
  await expect(page.getByTestId(ids.coverReplace)).toBeEnabled();
  await expect(page.getByTestId(ids.coverRemove)).toBeDisabled();
  await expect(page.getByTestId(ids.navFoot)).toContainText('Changes save as you go.');
  await expect(page.getByTestId(ids.guideWords)).toBeVisible();
});

// A 1×1 PNG, so the uploaded cover really loads in the browser.
const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

interface CoverCalls {
  uploads: string[];
  sets: unknown[];
  removes: number;
}

// The book header follows the cover the stubbed PUT/DELETE …/cover set; uploads answer `asset-1` unless `failUpload`.
async function stubCover(page: Page, failUpload = false): Promise<CoverCalls> {
  const calls: CoverCalls = { uploads: [], sets: [], removes: 0 };
  let coverUrl: string | null = null;
  const hymns = allBooks(library).find((b) => b.id === book);
  if (hymns === undefined) throw new Error('The library fixture has no Hymns for Machines.');
  await page.route(`**/api/books/${book}`, (route) =>
    route.fulfill({ json: { ...headerFor(hymns), coverUrl } }),
  );
  await page.route(`**/api/books/${book}/assets`, (route) => {
    calls.uploads.push(route.request().headers()['content-type'] ?? '');
    return failUpload
      ? route.fulfill({
          status: 400,
          json: { error: 'Upload a PNG, JPEG or WebP image.', code: 'validation' },
        })
      : route.fulfill({ status: 201, json: { id: 'asset-1' } });
  });
  await page.route(`**/api/books/${book}/cover`, (route) => {
    if (route.request().method() === 'PUT') {
      calls.sets.push(route.request().postDataJSON());
      coverUrl = '/api/assets/asset-1';
    } else {
      calls.removes += 1;
      coverUrl = null;
    }
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/assets/asset-1', (route) =>
    route.fulfill({ contentType: 'image/png', body: png }),
  );
  return calls;
}

async function chooseCoverFile(page: Page) {
  const chooser = page.waitForEvent('filechooser');
  await page.getByTestId(ids.coverReplace).click();
  await (await chooser).setFiles({ name: 'cover.png', mimeType: 'image/png', buffer: png });
}

test('Cover_WhenReplacedThenRemoved_ShouldUploadSetThenClearIt', async ({ mount, page }) => {
  await stubSettings(page);
  const calls = await stubCover(page);
  await open(mount);
  await expect(page.getByTestId(ids.coverRemove)).toBeDisabled();

  await chooseCoverFile(page);

  await expect(page.getByTestId(ids.cover)).toHaveAttribute('src', '/api/assets/asset-1');
  expect(calls.uploads).toHaveLength(1);
  expect(calls.uploads[0]).toContain('multipart/form-data');
  expect(calls.sets).toEqual([{ assetId: 'asset-1' }]);
  await expect(page.getByTestId(shellTestIds.bookCover)).toHaveAttribute(
    'src',
    '/api/assets/asset-1',
  );

  await page.getByTestId(ids.coverRemove).click();

  await expect(page.getByTestId(ids.cover)).not.toHaveAttribute('src');
  expect(calls.removes).toBe(1);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Cover removed.');
  await expect(page.getByTestId(ids.coverRemove)).toBeDisabled();
});

test('Cover_WhenUploadRejected_ShouldSayWhyAndKeepTheGeneratedCover', async ({ mount, page }) => {
  await stubSettings(page);
  const calls = await stubCover(page, true);
  await open(mount);
  await expect(page.getByTestId(ids.cover)).toBeVisible();

  await chooseCoverFile(page);

  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Upload a PNG, JPEG or WebP image.',
  );
  expect(calls.sets).toEqual([]);
  await expect(page.getByTestId(ids.cover)).not.toHaveAttribute('src');
  await expect(page.getByTestId(ids.coverReplace)).toBeEnabled();
});

test('Cover_WhenTheImageFailsToLoad_ShouldShowTheGeneratedCover', async ({ mount, page }) => {
  await stubSettings(page);
  const hymns = allBooks(library).find((b) => b.id === book);
  if (hymns === undefined) throw new Error('The library fixture has no Hymns for Machines.');
  await page.route(`**/api/books/${book}`, (route) =>
    route.fulfill({ json: { ...headerFor(hymns), coverUrl: '/api/assets/gone' } }),
  );
  await page.route('**/api/assets/gone', (route) =>
    route.fulfill({ status: 404, json: { error: 'Asset gone was not found.', code: 'not_found' } }),
  );
  await open(mount);

  // The generated cover (a box with the title) instead of a broken image; the upload can still be removed.
  await expect(page.getByTestId(ids.cover)).toHaveText('Hymns for Machines');
  await expect(page.getByTestId(ids.cover)).not.toHaveAttribute('src');
  await expect(page.getByTestId(ids.coverRemove)).toBeEnabled();
});

test('Series_WhenStandalone_ShouldExplainWhatPickingOneDoes', async ({ mount, page }) => {
  await stubSettings(page, {
    ...settings,
    seriesId: null,
    seriesName: null,
    seriesNumber: null,
    seriesOrder: [],
  });
  await open(mount);

  await expect(page.getByTestId(ids.standaloneHelp)).toContainText('Standalone. Pick a series');
});

test('Nav_WhenItemClicked_ShouldLinkToTheSectionHash', async ({ mount, page }) => {
  await stubSettings(page);
  await open(mount);

  await expect(page.getByTestId(ids.navItem('series'))).toHaveAttribute(
    'href',
    `/books/${book}/settings#series`,
  );
  await page.getByTestId(ids.navItem('writing')).click();
  await expect(page.getByTestId(ids.navItem('writing'))).toHaveAttribute('aria-current', 'true');
  await expect(page.getByTestId(ids.card('writing'))).toBeInViewport();
});

test('Title_WhenEditedAndBlurred_ShouldPatchOnlyTheTitle', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await open(mount);

  await page.getByTestId(ids.title).fill('Hymns, Revised');
  await page.getByTestId(ids.title).blur();

  await expect.poll(() => calls.patches).toEqual([{ title: 'Hymns, Revised' }]);
  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute('data-state', 'saved');
});

test('StyleGuide_WhenOpened_ShouldShowSourceAndPatchTheTypedTextOnBlur', async ({
  mount,
  page,
}) => {
  const calls = await stubSettings(page);
  await open(mount);

  const source = page.getByTestId(proseTestIds.sourceEditor('style-guide'));
  await expect(source).toHaveText('Close third.');
  await source.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' Past tense.');
  expect(calls.patches).toEqual([]);
  await page.getByTestId(ids.title).click();

  await expect.poll(() => calls.patches).toEqual([{ styleGuide: 'Close third. Past tense.' }]);
  await expect(page.getByTestId(ids.guideWords)).toHaveText('4 words');
});

test('StyleGuide_WhenToggledToRichAndBack_ShouldSendNothing', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await open(mount);

  await page.getByTestId(proseTestIds.modeRich('style-guide')).click();
  await expect(page.getByTestId(proseTestIds.editor)).toHaveText('Close third.');
  await page.getByTestId(proseTestIds.modeSource('style-guide')).click();
  await page.getByTestId(ids.title).click();

  expect(calls.patches).toEqual([]);
});

test('Title_WhenBlank_ShouldShowErrorAndSendNothing', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await open(mount);

  await page.getByTestId(ids.title).fill('   ');
  await page.getByTestId(ids.title).blur();

  await expect(page.getByTestId(ids.titleError)).toBeVisible();
  expect(calls.patches).toEqual([]);
});

test('Status_WhenChosen_ShouldSaveAtOnce', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await open(mount);

  await page.getByTestId(ids.statusOption('revising')).click();

  await expect.poll(() => calls.patches).toEqual([{ status: 'revising' }]);
});

test('Number_WhenChangedThenUndone_ShouldPutNewThenOldNumber', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await open(mount);

  await page.getByTestId(ids.numberSelect).selectOption('1');
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Book 001');
  await expect(page.getByTestId(ids.orderWas(first))).toHaveText('was 001');
  await page.getByTestId(editingTestIds.toastUndo).click();

  await expect.poll(() => calls.numbers).toEqual([{ number: 1 }, { number: 2 }]);
  await expect(page.getByTestId(ids.orderWas(first))).toHaveCount(0);
});

test('Number_WhenServerRejects_ShouldRevertAndSayWhy', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/series-number`, (route) =>
    route.fulfill({
      status: 400,
      json: { error: 'Number must be between 1 and 3.', code: 'validation' },
    }),
  );
  await open(mount);

  await page.getByTestId(ids.numberSelect).selectOption('3');

  await expect(page.getByTestId(ids.numberError)).toHaveText('Number must be between 1 and 3.');
  await expect(page.getByTestId(ids.numberSelect)).toHaveValue('2');
});

const preview: SeriesMovePreview = {
  bookId: book,
  title: 'Hymns for Machines',
  from: { id: series, name: 'Hymn Cycle', number: 2 },
  to: { id: null, name: null, number: null },
  steps: [
    { key: 'copyCategories', text: 'Copy 1 category from Hymn Cycle onto this book.' },
    { key: 'join', text: 'Leave the book standalone: it has no series number.' },
  ],
  notes: ["Hymn Cycle and its other books don't change."],
  warnings: ["Moving the book back won't merge the copies again."],
  targetSeriesSharedWith: 0,
  losingAccess: 0,
};

test('Series_WhenNoneChosen_ShouldAskFirstThenMoveOnConfirm', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await page.route(`**/api/books/${book}/series-move`, (route) => {
    calls.moves.push(route.request().postDataJSON());
    return route.fulfill({
      json: {
        ...settings,
        seriesId: null,
        seriesName: null,
        seriesNumber: null,
        seriesOrder: [],
        seriesGuide: null,
      },
    });
  });
  await open(mount);

  await page.getByTestId(ids.seriesSelect).selectOption('');
  await expect(page.getByTestId(ids.moveNote)).toContainText('Not moved yet');
  expect(calls.moves).toEqual([]);
  await page.getByTestId(ids.moveReview).click();
  await expect(page.getByTestId(ids.moveFromTo)).toContainText('Hymn Cycle · Book 002');
  await expect(page.getByTestId(ids.moveStep('copyCategories'))).toBeVisible();
  await expect(page.getByTestId(ids.moveWarning)).toContainText("won't merge");
  await page.getByTestId(ids.moveConfirm).click();

  await expect.poll(() => calls.moves).toEqual([{ seriesId: null }]);
  await expect(page.getByTestId(ids.moveNote)).toHaveCount(0);
  await expect(page.getByTestId(ids.seriesSelect)).toHaveValue('');
});

test('Series_WhenReviewCancelled_ShouldKeepTheNoteAndMoveNothing', async ({ mount, page }) => {
  const calls = await stubSettings(page);
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await open(mount);

  await page.getByTestId(ids.seriesSelect).selectOption(other);
  await page.getByTestId(ids.moveReview).click();
  await page.getByTestId(ids.moveCancel).click();

  await expect(page.getByTestId(ids.moveDialog)).toBeHidden();
  await expect(page.getByTestId(ids.moveNote)).toBeVisible();
  expect(calls.moves).toEqual([]);
});

test('Series_WhenMoveFails_ShouldShowTheErrorInTheDialog', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await page.route(`**/api/books/${book}/series-move`, (route) =>
    route.fulfill({ status: 400, json: { error: "That isn't a series.", code: 'validation' } }),
  );
  await open(mount);

  await page.getByTestId(ids.seriesSelect).selectOption('');
  await page.getByTestId(ids.moveReview).click();
  await page.getByTestId(ids.moveConfirm).click();

  await expect(page.getByTestId(ids.moveError)).toHaveText("That isn't a series.");
  await expect(page.getByTestId(ids.moveDialog)).toBeVisible();
});

test('ReviewMove_WhenOpenedByKeyboard_ShouldTakeFocusKeepTabInsideAndReturnFocusOnEscape', async ({
  mount,
  page,
}) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await open(mount);
  await page.getByTestId(ids.seriesSelect).selectOption(other);
  await page.getByTestId(ids.moveReview).focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByTestId(ids.moveDialog);
  await expect(dialog).toBeVisible();

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
  await page.keyboard.press('Escape');

  await expect(dialog).toBeHidden();
  await expect(page.getByTestId(ids.moveReview)).toBeFocused();
});

test('ReviewMove_WhenCancelPressed_ShouldReturnFocusToReview', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await open(mount);
  await page.getByTestId(ids.seriesSelect).selectOption(other);
  await page.getByTestId(ids.moveReview).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(ids.moveDialog)).toBeVisible();

  await page.getByTestId(ids.moveCancel).press('Enter');

  await expect(page.getByTestId(ids.moveDialog)).toBeHidden();
  await expect(page.getByTestId(ids.moveReview)).toBeFocused();
});

const publishState = (over: Partial<PublishState> = {}): PublishState => ({
  status: 'writing',
  wordCount: 1095,
  chapterCount: 3,
  atRiskCount: 0,
  exportRunning: false,
  snapshots: [],
  ...over,
});

test('Status_WhenPublishedConfirmed_ShouldPostPublishAndListTheSnapshot', async ({
  mount,
  page,
}) => {
  const calls = await stubSettings(page);
  let state = publishState();
  let posts = 0;
  await page.route(`**/api/books/${book}/publish`, async (route) => {
    if (route.request().method() === 'POST') {
      posts += 1;
      state = publishState({
        status: 'published',
        snapshots: [
          {
            id: 'snap-1',
            label: 'Published',
            whenLabel: '19 Oct 2026',
            words: 1095,
            state: 'succeeded',
            waiting: false,
            fileUrl: '/api/exports/snap-1/file',
          },
        ],
      });
    }
    return route.fulfill({ json: state });
  });
  await open(mount);

  await page.getByTestId(ids.statusOption('published')).click();
  await expect(page.getByTestId(ids.publishMessage)).toContainText('Words: 1,095.');
  await expect(page.getByTestId(ids.publishWarning)).toHaveText('0 sources at risk.');
  expect(posts).toBe(0);
  await page.getByTestId(ids.publishCancel).click();
  await expect(page.getByTestId(ids.publishDialog)).toBeHidden();
  expect(posts).toBe(0);
  expect(calls.patches).toEqual([]);

  await page.getByTestId(ids.statusOption('published')).click();
  await page.getByTestId(ids.publishConfirm).click();
  await expect(page.getByTestId(ids.statusOption('published'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(posts).toBe(1);
  await expect(page.getByTestId(ids.snapshotsHeading)).toHaveText('Snapshots (1)');
  await expect(page.getByTestId(ids.snapshot('snap-1'))).toContainText(
    'Published · 19 Oct 2026 · 1,095 words',
  );
});

test('Status_WhenPublishedWithRisks_ShouldWarnWithoutBlocking', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/publish`, (route) =>
    route.fulfill({ json: publishState({ atRiskCount: 3 }) }),
  );
  await open(mount);

  await page.getByTestId(ids.statusOption('published')).click();

  await expect(page.getByTestId(ids.publishWarning)).toHaveText(
    '3 sources are at risk. The book can be marked published, but Export for printer stays blocked.',
  );
  await expect(page.getByTestId(ids.publishConfirm)).toBeEnabled();
});

test('Status_WhenPublishedWithNoChapters_ShouldBlockConfirm', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/publish`, (route) =>
    route.fulfill({ json: publishState({ chapterCount: 0, wordCount: 0 }) }),
  );
  await open(mount);

  await page.getByTestId(ids.statusOption('published')).click();

  await expect(page.getByTestId(ids.publishMessage)).toHaveText(
    'Nothing to publish. Add a chapter first.',
  );
  await expect(page.getByTestId(ids.publishConfirm)).toBeDisabled();
});

test('Status_WhenAnExportRuns_ShouldSayThePublishWaits', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/publish`, (route) =>
    route.fulfill({ json: publishState({ exportRunning: true }) }),
  );
  await open(mount);

  await page.getByTestId(ids.statusOption('published')).click();

  await expect(page.getByTestId(ids.publishWaiting)).toContainText(
    'Waiting for the running export',
  );
});

test('NewEdition_WhenCreated_ShouldPostNewEditionWithTheDefaultTitle', async ({ mount, page }) => {
  await stubSettings(page);
  await page.route(`**/api/books/${book}/publish`, (route) =>
    route.fulfill({ json: publishState() }),
  );
  let body: unknown = null;
  await page.route(`**/api/books/${book}/new-edition`, (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({ json: { id: 'new-1', title: 'Hymns for Machines (2nd edition)' } });
  });
  await open(mount);

  await page.getByTestId(ids.newEdition).click();
  await expect(page.getByTestId(ids.duplicateTitle)).toHaveValue(
    'Hymns for Machines (2nd edition)',
  );
  await page.getByTestId(ids.duplicateConfirm).click();

  await expect(page.getByTestId(ids.duplicateDone)).toContainText(
    'Hymns for Machines (2nd edition)',
  );
  expect(body).toEqual({ title: 'Hymns for Machines (2nd edition)' });
});
