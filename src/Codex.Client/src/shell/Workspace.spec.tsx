import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../AppRoutes';
import { SlotHarness } from '../test-support/SlotHarness';
import { stubReferencesApi } from '../test-support/referenceStubs';
import { stubShellApi } from '../test-support/shellStubs';
import { libraryTestIds } from '../features/library/testIds';
import { settingsTestIds } from '../features/settings/testIds';
import { testIds } from '../testIds';
import { shellTestIds } from './shellTestIds';
import { layoutTestIds } from '../features/layout/testIds';
import { planTestIds } from '../features/plan/testIds';
import { refsTestIds } from '../features/references/testIds';

const hymns = library.series[0].books[1].id;

test('BookWorkspace_WhenUnknownBook_ShouldShowNotFound', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, {
    hooksConfig: { route: '/books/0199a000-0000-7000-8000-0000000000ff/plan' },
  });
  await expect(page.getByTestId(testIds.shell.errorUnknownBook)).toBeVisible();
  await page.getByTestId(shellTestIds.errorBackLink).click();
  await expect(page.getByTestId(testIds.shell.libraryPage)).toBeVisible();
});

test('BookBar_ShouldHideChatAndReview', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/write` } });
  const bar = page.getByTestId(testIds.shell.bookBar);
  await expect(bar).toBeVisible();
  for (const id of [
    testIds.shell.tabPlan,
    testIds.shell.tabWrite,
    testIds.shell.tabReferences,
    testIds.shell.tabLayout,
  ]) {
    await expect(page.getByTestId(id)).toBeVisible();
  }
  await expect(bar.getByRole('link', { name: 'Chat' })).toHaveCount(0);
  await expect(bar.getByRole('link', { name: 'Review' })).toHaveCount(0);
  await expect(bar.getByText('Chat')).toHaveCount(0);
  await expect(bar.getByText('Review')).toHaveCount(0);
  // Write has a real page now: the Write tab being current says the Write route opened.
  await expect(page.getByTestId(testIds.shell.tabWrite)).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();
});

test('BookTabs_WhenClicked_ShouldSwitchLanePage', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, hymns);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan` } });
  await page.getByTestId(testIds.shell.tabReferences).click();
  await expect(page.getByTestId(refsTestIds.page)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toHaveCount(0);
  await page.getByTestId(testIds.shell.tabSettings).click();
  await expect(page.getByTestId(settingsTestIds.page)).toBeVisible();
  // Layout has a real page now (L7.3): it shows once the book's layout state has loaded.
  await page.route('**/api/books/*/layout', (route) =>
    route.fulfill({ json: { template: 'novel-6x9', trim: '6x9', templates: [] } }),
  );
  await page.getByTestId(testIds.shell.tabLayout).click();
  await expect(page.getByTestId(layoutTestIds.page)).toBeVisible();
});

test('DeepLink_WhenHashInvalid_ShouldShowErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan#scene=abc` } });
  await expect(page.getByTestId(testIds.shell.errorBadLink)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.bookBar)).toBeVisible();
  await page.getByTestId(shellTestIds.errorRecoverLink).click();
  await expect(page.getByTestId(planTestIds.page)).toBeVisible();
});

test('DeepLink_WhenLibraryHashInvalid_ShouldShowErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/#series=bad id' } });
  await expect(page.getByTestId(testIds.shell.errorBadLink)).toBeVisible();
});

test('Route_WhenUnknownPath_ShouldShowNotFound', async ({ mount, page }) => {
  await mount(<AppRoutes />, { hooksConfig: { route: '/nowhere' } });
  await expect(page.getByTestId(shellTestIds.notFound)).toBeVisible();
});

test('Slots_WhenLanePageFillsThem_ShouldRender', async ({ mount, page }) => {
  await mount(<SlotHarness />);
  await expect(page.getByTestId(testIds.shell.badgeSlot)).toHaveText('3');
  await expect(page.getByTestId(testIds.shell.scopeSlot)).toContainText('Full manuscript');
  await expect(
    page.getByTestId(testIds.shell.formatSlot).getByRole('button', { name: 'Format' }),
  ).toBeVisible();
  await expect(
    page.getByTestId(testIds.shell.focusSlot).getByRole('button', { name: 'Focus' }),
  ).toBeVisible();
  await expect(page.getByTestId(testIds.shell.saveStatusSlot)).toHaveText('Saved');
  await expect(page.getByTestId(testIds.shell.badgeSlot)).toBeInViewport();
});

test('Palette_WhenCtrlK_ShouldOpenSlot', async ({ mount, page }) => {
  await mount(<SlotHarness />);
  await expect(page.getByTestId(testIds.shell.commandPalette)).toHaveCount(0);
  await page.keyboard.press('Control+k');
  const palette = page.getByTestId(testIds.shell.commandPalette);
  await expect(palette).toBeVisible();
  await expect(
    palette.getByTestId(testIds.shell.paletteSlot).getByLabel('Palette search'),
  ).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(palette).toHaveCount(0);
  // The book bar has no key cap (the mockup's bar ends with the avatar); the Library bar keeps it (Palette.spec).
  await expect(page.getByTestId(shellTestIds.paletteButton)).toHaveCount(0);
});

test('Theme_WhenToggled_ShouldSwitchTokens', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const surface = () =>
    page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--color-surface').trim(),
    );
  await page.evaluate(() => (document.documentElement.dataset.theme = 'light'));
  expect(await surface()).toBe('#f7f9fd');
  await page.evaluate(() => (document.documentElement.dataset.theme = 'dark'));
  expect(await surface()).toBe('#1f2839');
  await page.evaluate(() => delete document.documentElement.dataset.theme);
  await page.getByTestId(shellTestIds.paletteButton).click();
  await page.getByTestId(libraryTestIds.paletteAction('theme')).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', /^(light|dark)$/);
  const forced = await page.evaluate(() => document.documentElement.dataset.theme);
  expect(await surface()).toBe(forced === 'dark' ? '#1f2839' : '#f7f9fd');
  await expect(page.locator('body')).toHaveCSS('font-family', /Inter Variable/);
});

test('BookIndex_WhenOpened_ShouldRedirectToLastScreenElsePlan', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.evaluate((id) => window.localStorage.setItem(`codex.last.${id}`, 'write'), hymns);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}` } });
  // Write has a real page now: the Write tab being current says the Write route opened.
  await expect(page.getByTestId(testIds.shell.tabWrite)).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId(testIds.shell.errorBadLink)).toHaveCount(0);
});

test('Settings_WhenOpened_ShouldNotBecomeTheLastScreen', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, hymns);
  await page.evaluate((id) => window.localStorage.setItem(`codex.last.${id}`, 'write'), hymns);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/settings#letters` } });
  await expect(page.getByTestId(settingsTestIds.page)).toBeVisible();
  await page.getByTestId(testIds.shell.tabReferences).click();
  await expect(page.getByTestId(refsTestIds.page)).toBeVisible();
  await expect
    .poll(() => page.evaluate((id) => window.localStorage.getItem(`codex.last.${id}`), hymns))
    .toBe('references');
  await page.getByTestId(testIds.shell.tabSettings).click();
  await expect(page.getByTestId(settingsTestIds.page)).toBeVisible();
  expect(await page.evaluate((id) => window.localStorage.getItem(`codex.last.${id}`), hymns)).toBe(
    'references',
  );
});
