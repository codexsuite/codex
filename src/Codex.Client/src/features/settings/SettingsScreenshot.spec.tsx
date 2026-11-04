import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { BookSettings, PublicationSettings } from '@codex/core/api/settings';
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { settingsTestIds as ids } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

// Northbound is the library's standalone book, like the mockup's Book settings view.
const book = '0199a000-0000-7000-8000-000000000021';

const settings: BookSettings = {
  id: book,
  title: 'Northbound',
  authorName: 'Joel Dickson',
  status: 'writing',
  targetWords: 80000,
  wordCount: 72340,
  progressPercent: 90,
  seriesId: null,
  seriesName: null,
  seriesNumber: null,
  seriesOptions: [],
  seriesOrder: [],
  styleGuide:
    'First person, conversational, British spelling.\nPrefer stories from real teams over abstractions; name the team, change the details.',
  seriesGuide: null,
  archived: false,
  subtitle: null,
  description: null,
};

const publication: PublicationSettings = {
  bookId: book,
  seriesName: null,
  style: { value: 'chicago', inherited: false },
  rulesName: 'Default',
  quoteWordLimit: { value: 300, inherited: false },
  publisher: { value: null, inherited: false },
  plannedYear: { value: null, inherited: false },
  formats: { value: [], inherited: false },
  printRun: { value: null, inherited: false },
  territory: { value: null, inherited: false },
};

test('SettingsPage_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book}/settings`, (route) => route.fulfill({ json: settings }));
  await page.route(`**/api/books/${book}/publication-settings`, (route) =>
    route.fulfill({ json: publication }),
  );
  await page.route(`**/api/books/${book}/settings/rules-impact**`, (route) =>
    route.fulfill({ json: { limit: 300, riskCount: 0, sources: [] } }),
  );
  await page.route(`**/api/books/${book}/settings/layout`, (route) =>
    route.fulfill({ json: { template: null, riskCount: 0 } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book}/settings` } });

  await expect(page.getByTestId(ids.page)).toBeVisible();
  await expect(page.getByTestId(ids.title)).toHaveValue('Northbound');
  await expect(page.getByTestId(ids.progressText)).toHaveText('72,340 of 80,000 · 90%');
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('settings-default-light.png');

  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page).toHaveScreenshot('settings-default-dark.png');
});
