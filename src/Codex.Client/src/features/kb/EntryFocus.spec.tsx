import { expect, test } from '@playwright/experimental-ct-react';
import type { MountResult } from '@playwright/experimental-ct-react';
import type { ReactElement } from 'react';
import type { Page } from 'playwright';
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { kbTestIds } from './testIds';

const hymns = library.series[0].books[1].id;
const [ward] = kb.entries;
const people = kb.categories[0];
const wardEntry = { ...entry, id: ward.id, name: ward.name, categoryId: people.id };

type Mount = (component: ReactElement, options?: object) => Promise<MountResult>;

// The entry window and its Delete confirmation (#16 review section D, W4.8): the window takes focus when it opens,
// keeps Tab inside it, and gives focus back to the sidebar entry that opened it.

async function openFromTheSidebar(mount: Mount, page: Page) {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) => route.fulfill({ json: wardEntry }));
  await page.route('**/api/kb/entries/*/delete-impact', (route) =>
    route.fulfill({ json: { scenes: 2, notes: 0 } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan#kb=${ward.id}` } });
  const item = page.getByTestId(testIds.shell.kbItem(ward.id));
  await item.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
  return item;
}

test('EntryWindow_WhenOpenedFromTheSidebar_ShouldTakeFocusAndKeepTabInsideIt', async ({
  mount,
  page,
}) => {
  await openFromTheSidebar(mount, page);
  const dialog = page.getByTestId(kbTestIds.entryWindow);

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog, 30);
});

test('EntryWindow_WhenEscape_ShouldCloseAndReturnFocusToTheSidebarEntry', async ({
  mount,
  page,
}) => {
  const item = await openFromTheSidebar(mount, page);

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(kbTestIds.entryWindow)).toBeHidden();
  await expect(item).toBeFocused();
});

test('EntryWindow_WhenCloseButtonPressed_ShouldReturnFocusToTheSidebarEntry', async ({
  mount,
  page,
}) => {
  const item = await openFromTheSidebar(mount, page);

  await page.getByTestId(kbTestIds.entryWindowClose).press('Enter');

  await expect(page.getByTestId(kbTestIds.entryWindow)).toBeHidden();
  await expect(item).toBeFocused();
});

test('DeleteEntry_WhenEscape_ShouldCloseOnlyTheConfirmationAndReturnFocusToTheMenuButton', async ({
  mount,
  page,
}) => {
  await openFromTheSidebar(mount, page);
  await page.getByTestId(kbTestIds.entryMenu).focus();
  await page.keyboard.press('Enter');
  await page.getByTestId(kbTestIds.entryMenuDelete).focus();
  await page.keyboard.press('Enter');
  const confirmation = page.getByTestId(kbTestIds.deleteDialog);
  await expect(confirmation).toBeVisible();
  await expectFocusInside(confirmation);
  await expectFocusTrapped(page, confirmation, 6);

  await page.keyboard.press('Escape');

  await expect(confirmation).toBeHidden();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toBeVisible();
  await expect(page.getByTestId(kbTestIds.entryMenu)).toBeFocused();
});

test('DeleteEntry_WhenCancelPressed_ShouldReturnFocusToTheMenuButton', async ({ mount, page }) => {
  await openFromTheSidebar(mount, page);
  await page.getByTestId(kbTestIds.entryMenu).focus();
  await page.keyboard.press('Enter');
  await page.getByTestId(kbTestIds.entryMenuDelete).focus();
  await page.keyboard.press('Enter');

  await page.getByTestId(kbTestIds.deleteCancel).press('Enter');

  await expect(page.getByTestId(kbTestIds.deleteDialog)).toBeHidden();
  await expect(page.getByTestId(kbTestIds.entryMenu)).toBeFocused();
});

test('EntryMenu_WhenEscape_ShouldCloseTheMenuNotTheWindow', async ({ mount, page }) => {
  await openFromTheSidebar(mount, page);
  await page.getByTestId(kbTestIds.entryMenu).focus();
  await page.keyboard.press('Enter');
  await page.getByTestId(kbTestIds.entryMenuDelete).focus();

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(kbTestIds.entryMenuDelete)).toBeHidden();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toBeVisible();
  await expect(page.getByTestId(kbTestIds.entryMenu)).toBeFocused();
});
