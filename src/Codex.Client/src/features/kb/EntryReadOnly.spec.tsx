import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { kbTestIds } from './testIds';

// The Knowledge base below the write role (#80), and a series entry refused to a writer on one book.
const hymns = allBooks(library).find((b) => b.id === library.series[0].books[1].id)!;
const [ward] = kb.entries;
const people = kb.categories[0];
const wardEntry = { ...entry, id: ward.id, name: ward.name, categoryId: people.id };
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };
const [mentioned, plainText] = entry.usedIn;

async function openEntry(
  page: Page,
  mount: (c: React.JSX.Element, o?: object) => Promise<unknown>,
  role: string,
) {
  const patches: unknown[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${hymns.id}`, (route) =>
    route.fulfill({ json: { ...headerFor(hymns), role, owner } }),
  );
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) => {
    if (route.request().method() === 'PATCH') {
      patches.push(route.request().postDataJSON());
      return route.fulfill({
        status: 403,
        json: {
          error: 'Editing a series entry needs edit access to the series.',
          code: 'forbidden',
        },
      });
    }
    return route.fulfill({ json: wardEntry });
  });
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${hymns.id}/plan#kb=${ward.id}&entry=${ward.id}` },
  });
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
  return patches;
}

test('EntryWindow_WhenViewOnly_ShouldReadWithNothingToChange', async ({ mount, page }) => {
  const patches = await openEntry(page, mount, 'view');

  await expect(page.getByTestId(kbTestIds.entryName)).toHaveAttribute('readonly', '');
  await expect(page.getByTestId(kbTestIds.summary)).toHaveAttribute('readonly', '');
  await expect(
    page.getByTestId(kbTestIds.body).locator('[contenteditable]').first(),
  ).toHaveAttribute('contenteditable', 'false');
  await expect(page.getByTestId(kbTestIds.alwaysInContext)).toBeDisabled();
  await expect(page.getByTestId(kbTestIds.categoryChip)).toBeDisabled();
  for (const id of [
    kbTestIds.entryMenu,
    kbTestIds.addAlias,
    kbTestIds.aliasRemove(entry.aliases[0]),
    kbTestIds.addCustomField,
    kbTestIds.relatedAdd,
    kbTestIds.newEntryButton,
  ]) {
    await expect(page.getByTestId(id)).toHaveCount(0);
  }
  // The sidebar's entries do not drag onto scenes.
  await expect(page.getByTestId(testIds.shell.kbItem(ward.id))).toHaveAttribute(
    'draggable',
    'false',
  );

  await page.getByTestId(kbTestIds.tab('used-in')).click();
  await expect(page.getByTestId(kbTestIds.openInWrite(mentioned.sceneId))).toBeVisible();
  await expect(page.getByTestId(kbTestIds.makeMention(plainText.sceneId))).toHaveCount(0);

  await page.getByTestId(kbTestIds.tab('timeline')).click();
  await expect(page.getByTestId(kbTestIds.timelineNote(entry.timeline[0].id))).toBeVisible();
  await expect(page.getByTestId(kbTestIds.timelineEdit(entry.timeline[0].id))).toHaveCount(0);
  await expect(page.getByTestId(kbTestIds.timelineAdd)).toHaveCount(0);
  expect(patches).toEqual([]);
});

test('EntryWindow_WhenASaveIsForbidden_ShouldSayWhyAndPutTheTextBack', async ({ mount, page }) => {
  const patches = await openEntry(page, mount, 'write');
  const summary = page.getByTestId(kbTestIds.summary);
  await expect(summary).toHaveValue(entry.summary);

  await summary.fill('A writer on one book tries a series entry.');
  await page.getByTestId(kbTestIds.entryName).focus();

  await expect(page.getByTestId(editingTestIds.toast).last()).toHaveText(
    /Editing a series entry needs edit access to the series\./,
  );
  await expect(summary).toHaveValue(entry.summary);
  expect(patches).toHaveLength(1);
});
