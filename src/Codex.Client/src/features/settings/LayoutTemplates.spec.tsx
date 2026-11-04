import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { LayoutState, LayoutTemplate } from '@codex/core/api/layout';
import type { BookSettings, LayoutSettings, PublicationSettings } from '@codex/core/api/settings';
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { settingsTestIds as ids } from './testIds';

const book = '0199a000-0000-7000-8000-000000000012';
const fantasyVersion = '0199a000-0000-7000-8000-0000000f0001';
const forkTemplate = '0199a000-0000-7000-8000-0000000f0010';
const fork100 = '0199a000-0000-7000-8000-0000000f0011';
const fork110 = '0199a000-0000-7000-8000-0000000f0012';

const settings: BookSettings = {
  id: book,
  title: 'Hymns for Machines',
  authorName: null,
  status: 'writing',
  targetWords: null,
  wordCount: 0,
  progressPercent: null,
  seriesId: null,
  seriesName: null,
  seriesNumber: null,
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
  seriesName: null,
  style: { value: 'apa', inherited: false },
  rulesName: 'Default',
  quoteWordLimit: { value: 250, inherited: false },
  publisher: { value: null, inherited: false },
  plannedYear: { value: null, inherited: false },
  formats: { value: [], inherited: false },
  printRun: { value: null, inherited: false },
  territory: { value: null, inherited: false },
};

const layoutSettings: LayoutSettings = {
  template: {
    name: 'Fantasy RPG (fork)',
    description: null,
    isUserTemplate: true,
    pinnedVersion: '1.0.0',
    latestVersion: '1.1.0',
    trimSize: 'us-letter',
    trims: ['us-letter', 'a4'],
  },
  riskCount: 0,
};

const fantasy: LayoutTemplate = {
  slug: 'fantasy-rpg',
  name: 'Fantasy RPG (5e style)',
  version: '1.0.0',
  engine: 'tectonic',
  trims: ['us-letter', 'a4'],
  columns: 2,
  hasPreviewCss: true,
  options: [],
  notices: [],
  templateId: '0199a000-0000-7000-8000-0000000f0000',
  versionId: fantasyVersion,
  isUserTemplate: false,
  description: 'Two columns, parchment sidebars and stat blocks for tabletop books.',
  forkedFrom: null,
  latestVersion: null,
  latestVersionId: null,
  latestChangelog: null,
};

const fork: LayoutTemplate = {
  ...fantasy,
  slug: 'fantasy-rpg-fork',
  name: 'Fantasy RPG (fork)',
  options: [
    {
      key: 'drop-caps',
      label: 'Drop cap on each chapter',
      type: 'boolean',
      choices: [],
      default: true,
      value: true,
    },
  ],
  templateId: forkTemplate,
  versionId: fork100,
  isUserTemplate: true,
  description: null,
  forkedFrom: 'Fantasy RPG (5e style) v1.0.0',
  latestVersion: '1.1.0',
  latestVersionId: fork110,
  latestChangelog: ['Darker body text'],
};

const state: LayoutState = {
  template: 'fantasy-rpg-fork',
  trim: 'us-letter',
  templates: [fantasy, fork],
};

interface Calls {
  puts: unknown[];
  upgrades: unknown[];
  previews: unknown[];
  forks: { url: string; body: unknown }[];
  uploads: string[];
}

async function stub(page: Page): Promise<Calls> {
  const calls: Calls = { puts: [], upgrades: [], previews: [], forks: [], uploads: [] };
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book}/settings`, (route) => route.fulfill({ json: settings }));
  await page.route(`**/api/books/${book}/publication-settings`, (route) =>
    route.fulfill({ json: publication }),
  );
  await page.route(`**/api/books/${book}/settings/rules-impact**`, (route) =>
    route.fulfill({ json: { limit: 250, riskCount: 0, sources: [] } }),
  );
  await page.route(`**/api/books/${book}/settings/layout`, (route) =>
    route.fulfill({ json: layoutSettings }),
  );
  await page.route(`**/api/books/${book}/layout`, (route) => {
    if (route.request().method() === 'PUT') calls.puts.push(route.request().postDataJSON());
    return route.fulfill({ json: state });
  });
  await page.route(`**/api/books/${book}/layout/upgrade`, (route) => {
    calls.upgrades.push(route.request().postDataJSON());
    return route.fulfill({ json: state });
  });
  await page.route(`**/api/books/${book}/layout/preview`, (route) => {
    calls.previews.push(route.request().postDataJSON());
    return route.fulfill({
      json: {
        html: '<h1>The Fallen World</h1><p>Long ago the gods walked among mortals.</p>',
        css: 'body { background: #eee5ce; }',
        template: 'fantasy-rpg-fork',
        templateName: 'Fantasy RPG (fork)',
        templateVersion: '1.1.0',
        trim: 'us-letter',
        options: {},
        notices: [],
        flags: [],
        figures: [],
        contentHash: 'abc',
        renderedAt: '2026-10-22T10:00:00Z',
        templateVersionId: fork110,
      },
    });
  });
  await page.route('**/api/layout-template-versions/*/sample', (route) =>
    route.fulfill({
      json: {
        html: '<h1>Welcome</h1><p>Sample text.</p>',
        css: 'body { background: #eee5ce; }',
        trim: 'us-letter',
        widthIn: 8.5,
        heightIn: 11,
        marginTopIn: 0.5,
        marginBottomIn: 0.8,
        marginInnerIn: 0.75,
        marginOuterIn: 0.75,
        columns: 2,
      },
    }),
  );
  await page.route('**/api/layout-template-versions/*/fork', (route) => {
    calls.forks.push({ url: route.request().url(), body: route.request().postDataJSON() });
    return route.fulfill({ status: 201, json: {} });
  });
  await page.route('**/api/layout-templates', (route) => {
    calls.uploads.push(route.request().headers()['content-type'] ?? '');
    return route.fulfill({
      status: 400,
      json: {
        error: 'The template package has problems: The required file wrapper.tex is missing.',
        code: 'validation',
      },
    });
  });
  return calls;
}

const open = (mount: Parameters<Parameters<typeof test>[2]>[0]['mount']) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book}/settings#layout` } });

const showTemplates = (page: Page) => page.getByTestId(ids.layoutTemplatesToggle).click();

test('Picker_WhenLayoutLoads_ShouldShowEachTemplateAsACardWithItsThumbnailVersionAndOrigin', async ({
  mount,
  page,
}) => {
  await stub(page);
  await open(mount);
  await showTemplates(page);

  const own = page.getByTestId(ids.layoutTemplateCard('fantasy-rpg-fork'));
  const builtIn = page.getByTestId(ids.layoutTemplateCard('fantasy-rpg'));
  await expect(own).toContainText('Forked from Fantasy RPG (5e style) v1.0.0');
  await expect(own).toContainText('v1.0.0');
  await expect(own).toHaveAttribute('aria-current', 'true');
  await expect(page.getByTestId(ids.pageFrame(ids.layoutTemplateThumb('fantasy-rpg')))).toHaveCount(
    1,
  );
  await expect(page.getByTestId(ids.layoutTemplateUploadVersion('fantasy-rpg-fork'))).toBeVisible();
  await expect(page.getByTestId(ids.layoutTemplateArchive('fantasy-rpg-fork'))).toBeDisabled();
  await expect(page.getByTestId(ids.layoutTemplateUploadVersion('fantasy-rpg'))).toHaveCount(0);
  await expect(builtIn).toContainText('Two columns, parchment sidebars');
  await expect(page.getByTestId(ids.layoutTemplateDownload('fantasy-rpg-fork'))).toHaveAttribute(
    'href',
    `/api/layout-template-versions/${fork100}/package`,
  );
});

test('Upgrade_WhenApplied_ShouldShowTheChangelogAndRepinToTheNewerVersion', async ({
  mount,
  page,
}) => {
  const calls = await stub(page);
  await open(mount);

  await expect(page.getByTestId(ids.layoutChangelog)).toHaveText('Darker body text');
  await page.getByTestId(ids.layoutUpgradeApply).click();

  await expect.poll(() => calls.upgrades).toEqual([{ templateVersionId: fork110 }]);
});

test('Preview_WhenOpened_ShouldRenderTheNewerVersionWithoutApplyingIt', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.layoutUpgradePreview).click();

  await expect(page.getByTestId(ids.layoutPreviewDialog)).toContainText(
    'Previewing Fantasy RPG (fork) v1.1.0',
  );
  await expect(page.getByTestId(ids.pageFrame(ids.layoutPreviewPage))).toHaveCount(1);
  expect(calls.previews).toEqual([{ scope: 'book', templateVersionId: fork110 }]);
  await page.getByTestId(ids.layoutPreviewKeep).click();
  await expect(page.getByTestId(ids.layoutPreviewDialog)).toHaveCount(0);
  expect(calls.upgrades).toEqual([]);
});

test('Fork_WhenConfirmed_ShouldForkThatVersionWithTheName', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);
  await showTemplates(page);

  await page.getByTestId(ids.layoutTemplateFork('fantasy-rpg')).click();
  await expect(page.getByTestId(ids.layoutForkName)).toHaveValue('Fantasy RPG (5e style) (fork)');
  await page.getByTestId(ids.layoutForkName).fill('Forsaken RPG');
  await page.getByTestId(ids.layoutForkConfirm).click();

  await expect.poll(() => calls.forks.length).toBe(1);
  expect(calls.forks[0].url).toContain(`/api/layout-template-versions/${fantasyVersion}/fork`);
  expect(calls.forks[0].body).toEqual({ name: 'Forsaken RPG' });
  await expect(page.getByTestId(ids.layoutForkDialog)).toHaveCount(0);
});

test('Upload_WhenThePackageHasProblems_ShouldShowWhatTheServerSaid', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.layoutTemplateFile).setInputFiles({
    name: 'my-template.zip',
    mimeType: 'application/zip',
    buffer: Buffer.from('PK'),
  });

  await expect(page.getByTestId(ids.layoutTemplateError)).toHaveText(
    'The template package has problems: The required file wrapper.tex is missing.',
  );
  expect(calls.uploads[0]).toContain('multipart/form-data');
});

test('Option_WhenPressed_ShouldStoreTheValueForTheBook', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  await page.getByTestId(ids.layoutOptionChoice('drop-caps', 'false')).click();

  await expect
    .poll(() => calls.puts)
    .toEqual([{ template: 'fantasy-rpg-fork', options: { 'drop-caps': false } }]);
});
