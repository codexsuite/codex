import { expect, test } from '@playwright/experimental-ct-react';
import type { MountResult } from '@playwright/experimental-ct-react';
import type { ReactElement } from 'react';
import type { Page } from 'playwright';
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import lifecycle from '../../../../Codex.TestSupport/Fixtures/kb/entry-lifecycle.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { HashProbe } from '../../test-support/HashProbe';
import { stubShellApi } from '../../test-support/shellStubs';
import { kbTestIds } from './testIds';

const hymns = library.series[0].books[1].id;
const [ward, harbour] = kb.entries;
const people = kb.categories[0];
const wardEntry = { ...entry, id: ward.id, name: ward.name, categoryId: people.id };
const [conway] = entry.related;
const textUse = entry.usedIn[1];

type Mount = (component: ReactElement, options?: object) => Promise<MountResult>;

async function openWindow(mount: Mount, page: Page, current: object = wardEntry) {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) => route.fulfill({ json: current }));
  await mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${hymns}/plan#kb=${ward.id}&entry=${ward.id}` } },
  );
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
}

test('Related_WhenPicked_ShouldPostTheOtherEntryAndShowTheChip', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*/related', (route) =>
    route.fulfill({
      json: {
        ...wardEntry,
        related: [...entry.related, { id: harbour.id, name: harbour.name, categoryName: 'Places' }],
      },
    }),
  );
  await page.getByTestId(kbTestIds.relatedAdd).click();
  await page.getByTestId(kbTestIds.relatedSearch).fill('harb');
  const posted = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/related'));
  await page.getByTestId(kbTestIds.relatedOption(harbour.id)).click();
  expect((await posted).postDataJSON()).toEqual({ entryId: harbour.id });
  await expect(page.getByTestId(kbTestIds.relatedChip(harbour.id))).toContainText('Harbour');
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Linked');
});

test('Related_WhenRemoved_ShouldDeleteThePairAndDropTheChip', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*/related/*', (route) =>
    route.fulfill({ json: { ...wardEntry, related: [] } }),
  );
  const deleted = page.waitForRequest((r) => r.method() === 'DELETE');
  await page.getByTestId(kbTestIds.relatedChip(conway.id)).hover();
  await page.getByTestId(kbTestIds.relatedRemove(conway.id)).click();
  expect((await deleted).url()).toContain(`/related/${conway.id}`);
  await expect(page.getByTestId(kbTestIds.relatedChip(conway.id))).toHaveCount(0);
});

test('Related_WhenTheServerRefuses_ShouldShowTheMessage', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*/related', (route) =>
    route.fulfill({
      status: 400,
      json: { error: 'That entry belongs to another book or series.', code: 'validation' },
    }),
  );
  await page.getByTestId(kbTestIds.relatedAdd).click();
  await page.getByTestId(kbTestIds.relatedOption(harbour.id)).click();
  await expect(page.getByTestId(kbTestIds.relatedError)).toContainText('another book');
});

test('MakeMention_ShouldBeOfferedOnlyWhereTheNameIsPlainText', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.tab('used-in')).click();
  await expect(page.getByTestId(kbTestIds.makeMention(entry.usedIn[0].sceneId))).toHaveCount(0);
  await page.route('**/api/kb/entries/*/make-mention', (route) =>
    route.fulfill({ json: { ...wardEntry, usedIn: [entry.usedIn[0]] } }),
  );
  const posted = page.waitForRequest((r) => r.url().endsWith('/make-mention'));
  await page.getByTestId(kbTestIds.makeMention(textUse.sceneId)).click();
  expect((await posted).postDataJSON()).toEqual({ sceneId: textUse.sceneId });
  await expect(page.getByTestId(kbTestIds.useRow(textUse.sceneId))).toHaveCount(0);
});

async function openDeleteDialog(page: Page) {
  await page.route('**/api/kb/entries/*/delete-impact', (route) =>
    route.fulfill({ json: lifecycle.impact }),
  );
  await page.getByTestId(kbTestIds.entryMenu).click();
  await page.getByTestId(kbTestIds.entryMenuDelete).click();
}

test('Delete_ShouldWarnWithTheUseCountThenCloseAndOfferUndo', async ({ mount, page }) => {
  await openWindow(mount, page);
  await openDeleteDialog(page);
  await expect(page.getByTestId(kbTestIds.deleteMessage)).toContainText(
    'It is used in 2 scenes. Mentions turn into plain text, links and beat context are removed, and 1 timeline note is deleted.',
  );
  await page.route('**/api/kb/entries/*', (route) =>
    route.request().method() === 'DELETE'
      ? route.fulfill({ json: lifecycle.undo })
      : route.fulfill({ json: wardEntry }),
  );
  await page.getByTestId(kbTestIds.deleteConfirm).click();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toHaveCount(0);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText(`Deleted ${ward.name}`);

  const restored = page.waitForRequest((r) => r.url().endsWith('/restore'));
  await page.route('**/api/kb/entries/*/restore', (route) => route.fulfill({ json: wardEntry }));
  await page.getByTestId(editingTestIds.toastUndo).click();
  expect((await restored).postDataJSON()).toEqual(lifecycle.undo);
  await expect(page.getByTestId(editingTestIds.toast)).toHaveCount(0);
});

test('Delete_WhenUndoConflicts_ShouldSayWhy', async ({ mount, page }) => {
  await openWindow(mount, page);
  await openDeleteDialog(page);
  await page.route('**/api/kb/entries/*', (route) =>
    route.request().method() === 'DELETE'
      ? route.fulfill({ json: lifecycle.undo })
      : route.fulfill({ json: wardEntry }),
  );
  await page.getByTestId(kbTestIds.deleteConfirm).click();
  await page.route('**/api/kb/entries/*/restore', (route) =>
    route.fulfill({ status: 409, json: { error: 'A scene changed.', code: 'conflict' } }),
  );
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('stays deleted');
});

test('Delete_WhenCancelled_ShouldWriteNothing', async ({ mount, page }) => {
  await openWindow(mount, page);
  await openDeleteDialog(page);
  let deleted = false;
  await page.route('**/api/kb/entries/*', (route) => {
    if (route.request().method() === 'DELETE') deleted = true;
    return route.fulfill({ json: wardEntry });
  });
  await page.getByTestId(kbTestIds.deleteCancel).click();
  await expect(page.getByTestId(kbTestIds.deleteDialog)).toHaveCount(0);
  expect(deleted).toBe(false);
});
