import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { libraryTestIds } from './testIds';

type Mount = (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>;
type Page = Parameters<typeof stubShellApi>[0];

// The Library's modals (#16 review section D, W4.8): each takes focus when it opens, keeps it while open, and gives it
// back to the control that opened it however it closes (Esc, its × or its Cancel).

async function openLibrary(page: Page, mount: Mount) {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
}

// Opens the dialog the way a keyboard user does: focus the button, press Enter.
async function openByKeyboard(page: Page, openerId: string, dialogId: string) {
  await page.getByTestId(openerId).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(dialogId)).toBeVisible();
}

const closers = [
  ['Escape', null],
  ['CloseButton', libraryTestIds.newBookClose],
  ['CancelButton', libraryTestIds.newBookCancel],
] as const;

for (const [how, closeId] of closers) {
  test(`NewBook_When${how}_ShouldCloseAndReturnFocusToOpener`, async ({ mount, page }) => {
    await openLibrary(page, mount);
    await openByKeyboard(page, libraryTestIds.newBook, libraryTestIds.newBookDialog);

    if (closeId === null) await page.keyboard.press('Escape');
    else await page.getByTestId(closeId).press('Enter');

    await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeHidden();
    await expect(page.getByTestId(libraryTestIds.newBook)).toBeFocused();
  });
}

test('NewBook_WhenOpen_ShouldTakeFocusAndKeepTabInsideIt', async ({ mount, page }) => {
  await openLibrary(page, mount);
  await openByKeyboard(page, libraryTestIds.newBook, libraryTestIds.newBookDialog);
  const dialog = page.getByTestId(libraryTestIds.newBookDialog);

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
});

const importClosers = [
  ['Escape', null],
  ['CloseButton', libraryTestIds.importClose],
  ['CancelButton', libraryTestIds.importCancel],
] as const;

for (const [how, closeId] of importClosers) {
  test(`Import_When${how}_ShouldCloseAndReturnFocusToOpener`, async ({ mount, page }) => {
    await openLibrary(page, mount);
    await openByKeyboard(page, libraryTestIds.import, libraryTestIds.importDialog);

    if (closeId === null) await page.keyboard.press('Escape');
    else await page.getByTestId(closeId).press('Enter');

    await expect(page.getByTestId(libraryTestIds.importDialog)).toBeHidden();
    await expect(page.getByTestId(libraryTestIds.import)).toBeFocused();
  });
}

test('Import_WhenOpen_ShouldTakeFocusAndKeepTabInsideIt', async ({ mount, page }) => {
  await openLibrary(page, mount);
  await openByKeyboard(page, libraryTestIds.import, libraryTestIds.importDialog);
  const dialog = page.getByTestId(libraryTestIds.importDialog);

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
});

test('Palette_WhenOpenedWithTheShortcut_ShouldTakeFocusAndKeepTabInsideIt', async ({
  mount,
  page,
}) => {
  await openLibrary(page, mount);
  await expect(page.getByTestId(testIds.shell.tile(library.standalone[0].id))).toBeVisible();
  await page.keyboard.press('Control+k');
  const palette = page.getByTestId(testIds.shell.commandPalette);
  await expect(palette).toBeVisible();

  await expect(page.getByTestId(libraryTestIds.paletteInput)).toBeFocused();
  await expectFocusTrapped(page, palette);
});

test('Palette_WhenClosedWithEscape_ShouldReturnFocusToWhatHadIt', async ({ mount, page }) => {
  await openLibrary(page, mount);
  await page.getByTestId(libraryTestIds.newBook).focus();
  await page.keyboard.press('Control+k');
  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeHidden();
  await expect(page.getByTestId(libraryTestIds.newBook)).toBeFocused();
});

test('Palette_WhenClosedWithTheShortcutAgain_ShouldReturnFocusToWhatHadIt', async ({
  mount,
  page,
}) => {
  await openLibrary(page, mount);
  await page.getByTestId(libraryTestIds.import).focus();
  await page.keyboard.press('Control+k');
  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeVisible();

  await page.keyboard.press('Control+k');

  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeHidden();
  await expect(page.getByTestId(libraryTestIds.import)).toBeFocused();
});
