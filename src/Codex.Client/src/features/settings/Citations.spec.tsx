import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type {
  BookSettings,
  LayoutSettings,
  PublicationSettings,
  RulesImpact,
} from '@codex/core/api/settings';
import { AppRoutes } from '../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { stubShellApi } from '../../test-support/shellStubs';
import { settingsTestIds as ids } from './testIds';

const book = '0199a000-0000-7000-8000-000000000012';
const velocity = '0199c0de-0000-7000-8000-000d00000004';

const settings: BookSettings = {
  id: book,
  title: 'Hymns for Machines',
  authorName: null,
  status: 'writing',
  targetWords: null,
  wordCount: 0,
  progressPercent: null,
  seriesId: '0199a000-0000-7000-8000-0000000000a1',
  seriesName: 'Hymn Cycle',
  seriesNumber: 2,
  seriesOptions: [],
  seriesOrder: [],
  styleGuide: null,
  seriesGuide: null,
  archived: false,
  subtitle: null,
  description: null,
};

const publication: PublicationSettings = {
  bookId: book,
  seriesName: 'Hymn Cycle',
  style: { value: 'apa', inherited: true },
  rulesName: 'Default',
  quoteWordLimit: { value: 250, inherited: false },
  publisher: { value: 'Series House', inherited: true },
  plannedYear: { value: null, inherited: true },
  formats: { value: ['print'], inherited: true },
  printRun: { value: 5000, inherited: true },
  territory: { value: 'World English', inherited: true },
};

const impactAt = (limit: number): RulesImpact => ({
  limit,
  riskCount: limit < 342 ? 1 : 0,
  sources: [
    {
      referenceId: velocity,
      title: 'The Velocity Trap',
      number: 4,
      quotedWords: 342,
      reason: limit < 342 ? 'quote_over_limit' : null,
      atRisk: limit < 342,
      why: `342 of ${limit} words quoted`,
    },
  ],
});

interface Calls {
  patches: Record<string, unknown>[];
  deletes: string[];
  impacts: string[];
  archives: string[];
  duplicates: unknown[];
  exports: number;
}

async function stub(
  page: Page,
  options: { layout?: LayoutSettings; settings?: BookSettings } = {},
): Promise<Calls> {
  const calls: Calls = {
    patches: [],
    deletes: [],
    impacts: [],
    archives: [],
    duplicates: [],
    exports: 0,
  };
  let current = publication;
  let book_ = options.settings ?? settings;
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book}/settings`, (route) => route.fulfill({ json: book_ }));
  await page.route(`**/api/books/${book}/publication-settings`, async (route) => {
    if (route.request().method() === 'PATCH') {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      calls.patches.push(body);
      current = {
        ...current,
        ...(typeof body.style === 'string'
          ? {
              style: {
                value: body.style as PublicationSettings['style']['value'],
                inherited: false,
              },
            }
          : {}),
        ...(typeof body.quoteWordLimit === 'number'
          ? { quoteWordLimit: { value: body.quoteWordLimit, inherited: false } }
          : {}),
      };
    }
    return route.fulfill({ json: current });
  });
  await page.route(`**/api/books/${book}/publication-settings/*`, async (route) => {
    calls.deletes.push(route.request().url().split('/').pop() ?? '');
    current = { ...current, quoteWordLimit: { value: 300, inherited: true } };
    return route.fulfill({ json: current });
  });
  await page.route(`**/api/books/${book}/settings/rules-impact**`, async (route) => {
    const limit = Number(new URL(route.request().url()).searchParams.get('limit'));
    calls.impacts.push(String(limit));
    return route.fulfill({ json: impactAt(limit) });
  });
  await page.route(`**/api/books/${book}/settings/layout`, (route) =>
    route.fulfill({ json: options.layout ?? { template: null, riskCount: 0 } }),
  );
  await page.route(`**/api/books/${book}/archive`, (route) => {
    calls.archives.push('archive');
    book_ = { ...book_, archived: true };
    return route.fulfill({ json: book_ });
  });
  await page.route(`**/api/books/${book}/restore`, (route) => {
    calls.archives.push('restore');
    book_ = { ...book_, archived: false };
    return route.fulfill({ json: book_ });
  });
  await page.route(`**/api/books/${book}/duplicate`, (route) => {
    calls.duplicates.push(route.request().postDataJSON());
    return route.fulfill({ json: { id: '0199a000-0000-7000-8000-0000000000bb', title: 'Copy' } });
  });
  await page.route(`**/api/books/${book}/export-source`, (route) => {
    calls.exports += 1;
    return route.fulfill({
      json: {
        id: '0199a000-0000-7000-8000-0000000000cc',
        bookId: book,
        kind: 'source',
        state: 'queued',
        percent: 0,
        message: null,
        resultUrl: null,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    });
  });
  return calls;
}

const open = (mount: Parameters<Parameters<typeof test>[2]>[0]['mount'], hash = '') =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book}/settings${hash}` } });

test('Citations_WhenInASeries_ShouldChipEachValueAndWarnOnMissingLetterValues', async ({
  mount,
  page,
}) => {
  await stub(page);
  await open(mount);

  await expect(page.getByTestId(ids.citationStyleOption('apa'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByTestId(ids.chip('style'))).toHaveText('Series');
  await expect(page.getByTestId(ids.chip('quoteWordLimit'))).toHaveText('This book ×');
  await expect(page.getByTestId(ids.quoteLimit)).toHaveValue('250');
  await expect(page.getByTestId(ids.publisher)).toHaveValue('Series House');
  await expect(page.getByTestId(ids.format('print'))).toBeChecked();
  await expect(page.getByTestId(ids.letterMissing)).toHaveText('year');
  await expect(page.getByTestId(ids.letter)).toContainText('by Series House in print');
});

test('Style_WhenChosen_ShouldPatchAndMarkItThisBooks', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.citationStyleOption('harvard')).click();

  await expect.poll(() => calls.patches).toEqual([{ style: 'harvard' }]);
  await expect(page.getByTestId(ids.citationStyleOption('harvard'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByTestId(ids.chip('style'))).toHaveText('This book ×');
});

test('Limit_WhenTyped_ShouldShowTheLiveStripThenSaveOnBlur', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);
  await expect(page.getByTestId(ids.impactSummary)).toContainText('1 source at copyright risk');

  await page.getByTestId(ids.quoteLimit).fill('400');
  await expect(page.getByTestId(ids.impactSummary)).toHaveText('No sources at risk');
  expect(calls.patches).toEqual([]);
  await page.getByTestId(ids.quoteLimit).blur();

  await expect.poll(() => calls.patches).toEqual([{ quoteWordLimit: 400 }]);
  expect(calls.impacts).toContain('400');
});

test('Limit_WhenZero_ShouldShowAnErrorAndNotSave', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.quoteLimit).fill('0');
  await page.getByTestId(ids.quoteLimit).blur();

  await expect(page.getByTestId(ids.quoteLimitError)).toBeVisible();
  expect(calls.patches).toEqual([]);
});

test('Chip_WhenThisBookClicked_ShouldDeleteTheOverrideAndShowSeries', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.chipReset('quoteWordLimit')).click();

  await expect.poll(() => calls.deletes).toEqual(['quoteWordLimit']);
  await expect(page.getByTestId(ids.chip('quoteWordLimit'))).toHaveText('Series');
  await expect(page.getByTestId(ids.quoteLimit)).toHaveValue('300');
});

test('Layout_WhenBookHasATemplate_ShouldShowVersionTrimAndTheGate', async ({ mount, page }) => {
  await stub(page, {
    layout: {
      template: {
        name: 'Novel (6 x 9)',
        description: 'A single column book block.',
        isUserTemplate: false,
        pinnedVersion: '1.0.0',
        latestVersion: '1.1.0',
        trimSize: '6x9',
        trims: ['6x9', '5.5x8.5'],
      },
      riskCount: 3,
    },
  });
  await open(mount);

  await expect(page.getByTestId(ids.layoutTemplate)).toContainText('Novel (6 x 9)');
  await expect(page.getByTestId(ids.layoutVersion)).toHaveText('Pinned to v1.0.0');
  await expect(page.getByTestId(ids.layoutUpgrade)).toContainText('v1.1.0 available');
  await expect(page.getByTestId(ids.layoutTrim('6x9'))).toHaveAttribute('aria-current', 'true');
  await expect(page.getByTestId(ids.layoutGate)).toHaveText('3 copyright risks block print export');
});

test('Layout_WhenBookHasNoTemplate_ShouldSayNoTemplateChosen', async ({ mount, page }) => {
  await stub(page);
  await open(mount);

  await expect(page.getByTestId(ids.layoutNone)).toBeVisible();
  await expect(page.getByTestId(ids.layoutGate)).toHaveText('No copyright risks');
});

test('Archive_WhenConfirmed_ShouldShowTheWarnBarAndRestoreBringsItBack', async ({
  mount,
  page,
}) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.archive).click();
  await page.getByTestId(ids.archiveConfirm).click();

  await expect(page.getByTestId(ids.archivedBar)).toBeVisible();
  await page.getByTestId(ids.restore).click();
  await expect(page.getByTestId(ids.archivedBar)).toHaveCount(0);
  expect(calls.archives).toEqual(['archive', 'restore']);
});

test('Duplicate_WhenConfirmed_ShouldPostTheTitleAndLinkTheCopy', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.duplicate).click();
  await expect(page.getByTestId(ids.duplicateTitle)).toHaveValue('Hymns for Machines (copy)');
  await page.getByTestId(ids.duplicateConfirm).click();

  await expect(page.getByTestId(ids.duplicateDone)).toContainText('Copy');
  expect(calls.duplicates).toEqual([{ title: 'Hymns for Machines (copy)' }]);
});

test('ExportSource_WhenClicked_ShouldQueueTheJob', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.exportSource).click();

  await expect(page.getByTestId(ids.exportStatus)).toHaveText('Export queued.');
  expect(calls.exports).toBe(1);
});

// Focus in the Archive and Duplicate dialogs (#16 review section D, W4.8): each takes focus when it opens, keeps Tab
// inside it, and gives focus back to the button that opened it however it closes.
for (const [name, opener, dialog, cancel] of [
  ['Archive', ids.archive, ids.archiveDialog, ids.archiveCancel],
  ['Duplicate', ids.duplicate, ids.duplicateDialog, ids.duplicateCancel],
] as const) {
  test(`${name}_WhenOpenedByKeyboard_ShouldTakeFocusAndKeepTabInsideIt`, async ({
    mount,
    page,
  }) => {
    await stub(page);
    await open(mount);
    await page.getByTestId(opener).focus();
    await page.keyboard.press('Enter');

    await expect(page.getByTestId(dialog)).toBeVisible();
    await expectFocusInside(page.getByTestId(dialog));
    await expectFocusTrapped(page, page.getByTestId(dialog));
  });

  test(`${name}_WhenEscape_ShouldCloseAndReturnFocusToTheOpener`, async ({ mount, page }) => {
    await stub(page);
    await open(mount);
    await page.getByTestId(opener).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId(dialog)).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(page.getByTestId(dialog)).toBeHidden();
    await expect(page.getByTestId(opener)).toBeFocused();
  });

  test(`${name}_WhenCancelPressed_ShouldCloseAndReturnFocusToTheOpener`, async ({
    mount,
    page,
  }) => {
    await stub(page);
    await open(mount);
    await page.getByTestId(opener).focus();
    await page.keyboard.press('Enter');

    await page.getByTestId(cancel).press('Enter');

    await expect(page.getByTestId(dialog)).toBeHidden();
    await expect(page.getByTestId(opener)).toBeFocused();
  });
}
