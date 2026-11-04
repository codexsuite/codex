import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../../AppRoutes';
import type { LookupResult } from '@codex/core/api/referenceLookup';
import { stubReferencesApi, stubSources } from '../../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../../test-support/shellStubs';
import { refsTestIds } from '../testIds';

const book = allBooks(library)[0];
const newSource = '99999999-0000-4000-8000-000000000009';

const result = (extra: Partial<LookupResult>): LookupResult => ({
  outcome: 'not_found',
  value: '',
  source: null,
  existingId: null,
  existingTitle: null,
  existingByline: null,
  redirectTo: null,
  ...extra,
});

/** Answers lookup and add; returns the bodies the page posted. */
async function stubAdd(page: Page, lookup: LookupResult | { status: number; error: string }) {
  const posted = {
    lookup: [] as unknown[],
    add: [] as unknown[],
    import: [] as unknown[],
    preview: [] as unknown[],
  };
  await page.route(`**/api/books/${book.id}/references/lookup`, async (route) => {
    posted.lookup.push(route.request().postDataJSON());
    if ('status' in lookup) {
      await route.fulfill({
        status: lookup.status,
        json: { error: lookup.error, code: 'validation' },
      });
    } else {
      await route.fulfill({ json: lookup });
    }
  });
  await page.route(`**/api/books/${book.id}/references`, async (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    posted.add.push(route.request().postDataJSON());
    await route.fulfill({ status: 201, json: { id: newSource, existingId: null } });
  });
  const importResult = {
    created: 1,
    skipped: 1,
    skippedTitles: ['Extreme Programming Explained'],
    duplicates: 1,
    untitled: 0,
    skippedReasons: ['already in References'],
  };
  await page.route(`**/api/books/${book.id}/references/import`, async (route) => {
    posted.import.push(route.request().postDataJSON());
    await route.fulfill({ json: importResult });
  });
  await page.route(`**/api/books/${book.id}/references/import/preview`, async (route) => {
    posted.preview.push(route.request().postDataJSON());
    await route.fulfill({ json: importResult });
  });
  return posted;
}

async function open(
  mount: Parameters<Parameters<typeof test>[2]>[0]['mount'],
  page: Page,
  hash = '#new',
) {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/references${hash}` } });
}

test('AddSource_WhenHashIsNew_ShouldOpenTheFormAndWhenAnIdentifierIsFoundShouldAddAndSelectIt', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(
    page,
    result({
      outcome: 'found',
      value: '10.1000/xyz123',
      source: {
        title: 'Sleep and Memory',
        authors: ['Ada Lovelace'],
        year: 2019,
        publisher: 'Journal',
        url: 'https://doi.org/10.1000/xyz123',
      },
    }),
  );
  await open(mount, page);

  await page.getByTestId(refsTestIds.addInput).fill('doi:10.1000/xyz123');
  await page.getByTestId(refsTestIds.addLookup).click();

  await expect(page.getByTestId(refsTestIds.addForm)).toHaveCount(0);
  expect(posted.lookup).toEqual([{ kind: 'doi', value: 'doi:10.1000/xyz123' }]);
  expect(posted.add[0]).toMatchObject({
    via: 'doi',
    details: {
      kind: 'paper',
      title: 'Sleep and Memory',
      authorsText: 'Lovelace, Ada',
      year: 2019,
      doi: '10.1000/xyz123',
    },
  });
  await expect(page.getByTestId(refsTestIds.panel)).toBeVisible();
});

test('AddSource_WhenTheBookAlreadyHasIt_ShouldOfferTheExistingSourceInsteadOfLookUp', async ({
  mount,
  page,
}) => {
  const existing = stubSources[1];
  const posted = await stubAdd(
    page,
    result({
      outcome: 'existing',
      value: '9780140449136',
      existingId: existing.id,
      existingTitle: existing.title,
      existingByline: 'Brooks · 1975',
    }),
  );
  await open(mount, page);

  await page.getByTestId(refsTestIds.addInput).fill('978-0-14-044913-6');
  await page.getByTestId(refsTestIds.addLookup).click();

  await expect(page.getByTestId(refsTestIds.addMatchTitle)).toHaveText(existing.title);
  await expect(page.getByTestId(refsTestIds.addLookup)).toHaveCount(0);
  await page.getByTestId(refsTestIds.addUseExisting).click();
  await expect(page.getByTestId(refsTestIds.row(existing.id))).toHaveAttribute(
    'aria-selected',
    'true',
  );
  expect(posted.add).toEqual([]);
});

test('AddSource_WhenNothingIsFound_ShouldOpenTheFieldsWithTheIdentifierFilledIn', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(page, result({ outcome: 'not_found', value: '9780306406157' }));
  await open(mount, page);

  await page.getByTestId(refsTestIds.addInput).fill('0-306-40615-2');
  await page.getByTestId(refsTestIds.addLookup).click();

  await expect(page.getByTestId(refsTestIds.addNotFound)).toBeVisible();
  await expect(page.getByTestId(refsTestIds.addField('isbn'))).toHaveValue('9780306406157');
  await expect(page.getByTestId(refsTestIds.addSave)).toBeDisabled();
  await page.getByTestId(refsTestIds.addField('title')).fill('A hand-entered book');
  await page.getByTestId(refsTestIds.addSave).click();

  await expect(page.getByTestId(refsTestIds.addForm)).toHaveCount(0);
  expect(posted.add[0]).toMatchObject({
    via: null,
    details: { kind: 'book', title: 'A hand-entered book', isbn: '9780306406157' },
  });
});

test('AddSource_WhenTheTextIsNotAnIdentifier_ShouldSayWhySendNothingAndOfferHand', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(page, result({}));
  await open(mount, page);

  await page.getByTestId(refsTestIds.addInput).fill('The Lean Startup');
  await page.getByTestId(refsTestIds.addLookup).click();

  await expect(page.getByTestId(refsTestIds.addError)).toHaveText(
    'Enter an ISBN, DOI or web address.',
  );
  expect(posted.lookup).toEqual([]);
  await page.getByTestId(refsTestIds.addManual).click();
  await expect(page.getByTestId(refsTestIds.addManualForm)).toBeVisible();
});

test('AddSource_WhenTheServerRefusesTheAddress_ShouldShowItsMessageAndKeepTheForm', async ({
  mount,
  page,
}) => {
  await stubAdd(page, {
    status: 400,
    error: 'The address intranet.test is not on the public internet, so it cannot be looked up.',
  });
  await open(mount, page);

  await page.getByTestId(refsTestIds.addInput).fill('https://intranet.test/secret');
  await page.getByTestId(refsTestIds.addLookup).click();

  await expect(page.getByTestId(refsTestIds.addError)).toContainText('not on the public internet');
  await expect(page.getByTestId(refsTestIds.addForm)).toBeVisible();
});

test('AddSource_WhenCancelled_ShouldTakeTheHashAway', async ({ mount, page }) => {
  await stubAdd(page, result({}));
  await open(mount, page);

  await page.getByTestId(refsTestIds.addCancel).click();

  await expect(page.getByTestId(refsTestIds.addForm)).toHaveCount(0);
  await page.getByTestId(refsTestIds.addOpen).click();
  await expect(page.getByTestId(refsTestIds.addForm)).toBeVisible();
});

test('Import_WhenABibFileIsChosen_ShouldPreviewItsEntriesPostThemAndReportSkips', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(page, result({}));
  await open(mount, page, '');

  await page.getByTestId(refsTestIds.addImportOpen).click();
  await page.getByTestId(refsTestIds.addImportFile).setInputFiles({
    name: 'library.bib',
    mimeType: 'text/plain',
    buffer: Buffer.from(
      '@book{beck1999, author={Beck, Kent}, title={Extreme Programming Explained}, year={1999}}\n@book{ries2011, author={Ries, Eric}, title={The Lean Startup}, year={2011}}',
    ),
  });

  await expect(page.getByTestId(refsTestIds.addImportSummary)).toHaveText(
    '1 new · 1 already in References',
  );
  await expect(page.getByTestId(refsTestIds.addImportSkipped)).toContainText(
    'Extreme Programming Explained: already in References',
  );
  expect(posted.import).toHaveLength(0);
  await page.getByTestId(refsTestIds.addImportSubmit).click();

  await expect(page.getByTestId(refsTestIds.addImportResult)).toHaveText('1 added');
  expect(posted.import[0]).toMatchObject({
    items: [
      { provider: 'bibtex', key: 'beck1999' },
      { provider: 'bibtex', key: 'ries2011' },
    ],
  });
});

test('Import_WhenARisFileIsCutOff_ShouldListTheProblemAndImportTheCompleteRecords', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(page, result({}));
  await open(mount, page, '');

  await page.getByTestId(refsTestIds.addImportOpen).click();
  await page.getByTestId(refsTestIds.addImportFile).setInputFiles({
    name: 'export.ris',
    mimeType: 'text/plain',
    buffer: Buffer.from('TY  - BOOK\nTI  - Complete\nER  - \nTY  - BOOK\nTI  - Cut off'),
  });

  await expect(page.getByTestId(refsTestIds.addImportSummary)).toContainText('1 new');
  await expect(page.getByTestId(refsTestIds.addImportProblem)).toHaveText(
    'Line 4: a record has no ER line, left out.',
  );
  await page.getByTestId(refsTestIds.addImportSubmit).click();
  await expect(page.getByTestId(refsTestIds.addImportResult)).toBeVisible();
  expect(posted.import[0]).toMatchObject({
    items: [{ provider: 'ris', details: { title: 'Complete' } }],
  });
});

test('Import_WhenTheFileHoldsNothingReadable_ShouldKeepImportDisabled', async ({ mount, page }) => {
  await stubAdd(page, result({}));
  await open(mount, page, '');

  await page.getByTestId(refsTestIds.addImportOpen).click();
  await page.getByTestId(refsTestIds.addImportFile).setInputFiles({
    name: 'notes.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('just some notes'),
  });

  await expect(page.getByTestId(refsTestIds.addImportSummary)).toHaveText('0 entries found');
  await expect(page.getByTestId(refsTestIds.addImportSubmit)).toBeDisabled();
});

test('Import_WhenACslJsonFileIsChosen_ShouldReadItsItemsAsCsl', async ({ mount, page }) => {
  const posted = await stubAdd(page, result({}));
  await open(mount, page, '');

  await page.getByTestId(refsTestIds.addImportOpen).click();
  await page.getByTestId(refsTestIds.addImportFile).setInputFiles({
    name: 'library.json',
    mimeType: 'application/json',
    buffer: Buffer.from(
      JSON.stringify([
        {
          id: 'fowler2018',
          type: 'book',
          title: 'Refactoring',
          author: [{ family: 'Fowler', given: 'Martin' }],
          issued: { 'date-parts': [[2018]] },
        },
      ]),
    ),
  });

  await expect(page.getByTestId(refsTestIds.addImportSummary)).toContainText('new');
  expect(posted.preview[0]).toMatchObject({
    items: [{ provider: 'csl', key: 'fowler2018', details: { title: 'Refactoring', year: 2018 } }],
  });
});

test('Import_WhenTheJsonIsNotCsl_ShouldSayWhatCouldNotBeReadAndWriteNothing', async ({
  mount,
  page,
}) => {
  const posted = await stubAdd(page, result({}));
  await open(mount, page, '');

  await page.getByTestId(refsTestIds.addImportOpen).click();
  await page.getByTestId(refsTestIds.addImportFile).setInputFiles({
    name: 'broken.json',
    mimeType: 'application/json',
    buffer: Buffer.from('{ not json'),
  });

  await expect(page.getByTestId(refsTestIds.addImportProblem)).toContainText('not valid JSON');
  await expect(page.getByTestId(refsTestIds.addImportSubmit)).toBeDisabled();
  expect(posted.preview).toHaveLength(0);
  expect(posted.import).toHaveLength(0);
});
