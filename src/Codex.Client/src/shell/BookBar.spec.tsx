import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../AppRoutes';
import { editingTestIds } from '../editing/testIds';
import { layoutTestIds } from '../features/layout/testIds';
import { wordsLabel } from '@codex/core/features/library/bookFacts';
import type { Outline } from '@codex/core/api/structure';
import type { LayoutState } from '@codex/core/api/layout';
import { allBooks, headerFor, stubShellApi } from '../test-support/shellStubs';
import { testIds } from '../testIds';
import { shellTestIds } from './shellTestIds';
import type { Page } from 'playwright';

const book = allBooks(library)[0];

async function stubBadge(page: Page, count: number | null, tone: string | null) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({
      json: { ...headerFor(book), referenceBadgeCount: count, referenceBadgeTone: tone },
    }),
  );
}

test('LayoutBadge_WhenCopyrightRisks_ShouldShowTintedDangerChip', async ({ mount, page }) => {
  await stubBadge(page, 2, 'danger');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  const badge = page.getByTestId(testIds.shell.tabLayout).getByTestId(shellTestIds.layoutBadge);
  await expect(badge).toHaveText('2');
  await expect(badge).toHaveCSS('color', 'rgb(194, 53, 59)');
  // A tinted chip, not the References badge's solid fill.
  const background = await badge.evaluate((el) => getComputedStyle(el).backgroundColor);
  expect(background).not.toBe('rgb(194, 53, 59)');
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
});

for (const [count, tone] of [
  [3, 'warn'],
  [0, null],
  [null, null],
] as const) {
  test(`LayoutBadge_WhenNothingBlocksExport_ShouldBeHidden ${String(count)} ${String(tone)}`, async ({
    mount,
    page,
  }) => {
    await stubBadge(page, count, tone);
    await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

    await expect(page.getByTestId(testIds.shell.tabLayout)).toBeVisible();
    await expect(page.getByTestId(shellTestIds.layoutBadge)).toHaveCount(0);
  });
}

test('BookBar_ShouldPutBackAndSettingsIconsBeforeTheBookAndNoThemeToggle', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  const box = (id: string) => page.getByTestId(id).boundingBox();
  const [back, settings, cover, layout] = await Promise.all([
    box(testIds.shell.backLink),
    box(testIds.shell.tabSettings),
    box(shellTestIds.bookCover),
    box(testIds.shell.tabLayout),
  ]);
  expect(back!.width).toBe(22);
  expect(settings!.width).toBe(22);
  expect(back!.x).toBeLessThan(settings!.x);
  expect(settings!.x).toBeLessThan(cover!.x);
  expect(cover!.x).toBeLessThan(layout!.x);
  await expect(page.getByTestId(testIds.shell.backLink).locator('svg')).toHaveCount(1);
  await expect(page.getByTestId(testIds.shell.tabSettings).locator('svg')).toHaveCount(1);
  await expect(
    page.getByTestId(testIds.shell.bookBar).getByTestId(shellTestIds.themeToggle),
  ).toHaveCount(0);
});

test('BookBar_OnSettings_ShouldMarkTheGearCurrent', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/settings` } });

  await expect(page.getByTestId(testIds.shell.tabSettings)).toHaveAttribute('aria-current', 'page');
});

test('LayoutChrome_ShouldFillStatsAndSaveStatus', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  await expect(
    page.getByTestId(testIds.shell.scopeSlot).getByTestId(layoutTestIds.stats),
  ).toHaveText(wordsLabel(book.wordCount));
  await expect(
    page.getByTestId(testIds.shell.saveStatusSlot).getByTestId(editingTestIds.saveStatus),
  ).toHaveText('Saved');
});

test('SidebarToggle_OnLayout_ShouldShowAndHideTheKnowledgeBaseSidebar', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  const toggle = page.getByTestId(shellTestIds.sidebarToggle);
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toHaveCount(0);
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toHaveCount(0);
});

test('SidebarToggle_OnPlan_ShouldHideAndShowTheKnowledgeBaseSidebar', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

  const toggle = page.getByTestId(shellTestIds.sidebarToggle);
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toHaveCount(0);
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();
});

test('LayoutChrome_WhenLayoutAndOutlineLoad_ShouldEstimatePages', async ({ mount, page }) => {
  await stubShellApi(page, library);
  const layout: LayoutState = {
    template: 'novel-6x9',
    trim: '6x9',
    templates: [
      {
        slug: 'novel-6x9',
        name: 'Novel (6 x 9)',
        version: '1.0.0',
        engine: 'tectonic',
        trims: ['6x9', '5.5x8.5'],
        columns: 1,
        hasPreviewCss: false,
        notices: [],
        options: [],
      },
    ],
  };
  const scene = (id: string, words: number) => ({
    id,
    number: '1',
    title: null,
    summary: null,
    status: 'draft',
    position: 'a',
    words,
    hasPendingDraft: false,
    labels: [],
    linkedEntries: [],
  });
  const outline: Outline = {
    bookId: book.id,
    acts: [
      {
        id: 'a1',
        title: '',
        position: 'a',
        chapters: [
          {
            id: 'c1',
            number: 1,
            title: 'One',
            slug: 'one',
            position: 'a',
            archived: false,
            archivedSceneCount: 0,
            scenes: [scene('s1', 200), scene('s2', 100)],
            opener: null,
          },
        ],
        opener: null,
      },
    ],
  };
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/layout`, (route) => route.fulfill({ json: layout }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  // Four front-matter pages, then 300 words at 125 a page.
  await expect(
    page.getByTestId(testIds.shell.scopeSlot).getByTestId(layoutTestIds.stats),
  ).toHaveText(`${wordsLabel(book.wordCount)} · ≈ 7 pages in Novel 6×9`);
});

test('Avatar_OnEveryBookScreen_ShouldShowTheUsersInitialsInPlaceOfTheKeyCap', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const avatar = page.getByTestId(testIds.shell.bookBar).getByTestId(shellTestIds.avatar);
  await expect(avatar).toHaveText('JD');
  await expect(avatar).toHaveAttribute('title', 'Joel Dickson');
  await expect(page.getByTestId(shellTestIds.paletteButton)).toHaveCount(0);
});
