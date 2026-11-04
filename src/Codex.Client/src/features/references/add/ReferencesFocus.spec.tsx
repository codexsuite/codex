import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../../test-support/dialogFocus';
import { stubReferencesApi, stubSources } from '../../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../../test-support/shellStubs';
import { refsTestIds } from '../testIds';

const book = allBooks(library)[0];
const risk = stubSources[2];

// References' Add source form, Import dialog and Remove confirmation (#16 review section D, W4.8): each takes focus
// when it opens, keeps Tab inside it, and gives focus back to the control that opened it however it closes.

async function openReferences(
  mount: (node: React.JSX.Element, options: object) => Promise<unknown>,
  page: Page,
  hash = '',
) {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/references${hash}` } });
  await expect(page.getByTestId(refsTestIds.addOpen)).toBeVisible();
}

async function pressOpener(page: Page, openerId: string, dialogId: string) {
  await page.getByTestId(openerId).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(dialogId)).toBeVisible();
}

test('AddSource_WhenOpenedByKeyboard_ShouldFocusTheFieldAndKeepTabInsideTheForm', async ({
  mount,
  page,
}) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addOpen, refsTestIds.addForm);

  await expect(page.getByTestId(refsTestIds.addInput)).toBeFocused();
  await expectFocusTrapped(page, page.getByTestId(refsTestIds.addForm));
});

test('AddSource_WhenEscape_ShouldCloseAndReturnFocusToAddSource', async ({ mount, page }) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addOpen, refsTestIds.addForm);

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(refsTestIds.addForm)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.addOpen)).toBeFocused();
});

test('AddSource_WhenCancelPressed_ShouldCloseAndReturnFocusToAddSource', async ({
  mount,
  page,
}) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addOpen, refsTestIds.addForm);

  await page.getByTestId(refsTestIds.addCancel).press('Enter');

  await expect(page.getByTestId(refsTestIds.addForm)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.addOpen)).toBeFocused();
});

test('Import_WhenOpenedByKeyboard_ShouldTakeFocusAndKeepTabInsideIt', async ({ mount, page }) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addImportOpen, refsTestIds.addImportDialog);
  const dialog = page.getByTestId(refsTestIds.addImportDialog);

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
});

test('Import_WhenEscape_ShouldCloseAndReturnFocusToImport', async ({ mount, page }) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addImportOpen, refsTestIds.addImportDialog);

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(refsTestIds.addImportDialog)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.addImportOpen)).toBeFocused();
});

test('Import_WhenCancelPressed_ShouldCloseAndReturnFocusToImport', async ({ mount, page }) => {
  await openReferences(mount, page);
  await pressOpener(page, refsTestIds.addImportOpen, refsTestIds.addImportDialog);

  await page.getByTestId(refsTestIds.addImportClose).press('Enter');

  await expect(page.getByTestId(refsTestIds.addImportDialog)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.addImportOpen)).toBeFocused();
});

test('RemoveSource_WhenAsked_ShouldFocusCancelAndKeepTabInsideTheQuestion', async ({
  mount,
  page,
}) => {
  await openReferences(mount, page, `#${risk.id}`);
  await page.getByTestId(refsTestIds.remove).focus();
  await page.keyboard.press('Enter');
  const question = page.getByRole('alertdialog');

  await expect(page.getByTestId(refsTestIds.removeCancel)).toBeFocused();
  await expectFocusTrapped(page, question, 6);
});

test('RemoveSource_WhenEscape_ShouldCloseAndReturnFocusToRemoveSource', async ({ mount, page }) => {
  await openReferences(mount, page, `#${risk.id}`);
  await page.getByTestId(refsTestIds.remove).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(refsTestIds.removeConfirm)).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(refsTestIds.removeConfirm)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.remove)).toBeFocused();
});

test('RemoveSource_WhenCancelPressed_ShouldCloseAndReturnFocusToRemoveSource', async ({
  mount,
  page,
}) => {
  await openReferences(mount, page, `#${risk.id}`);
  await page.getByTestId(refsTestIds.remove).focus();
  await page.keyboard.press('Enter');

  await page.getByTestId(refsTestIds.removeCancel).press('Enter');

  await expect(page.getByTestId(refsTestIds.removeConfirm)).toBeHidden();
  await expect(page.getByTestId(refsTestIds.remove)).toBeFocused();
});
