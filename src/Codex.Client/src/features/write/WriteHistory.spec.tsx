import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const cached = manuscript.scenes.find((s) => s.sceneId === first.id)!;

const revision = (id: string, label: string, body: string, words: number, current = false) => ({
  id,
  source: 'autosave',
  label,
  detail: null,
  createdAt: '2026-01-01T14:02:00Z',
  timeLabel: '12m ago',
  clockLabel: '14:02',
  words,
  body,
  current,
});

async function stubHistory(page: Page, archived = false) {
  const restores: string[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/scenes/${first.id}/prose/revisions`, (route) =>
    route.fulfill({
      json: {
        sceneId: first.id,
        archived,
        version: cached.version,
        currentWords: 5,
        body: 'one two three four five',
        revisions: [
          revision('r2', 'Edited', 'one two three four five', 5, true),
          revision('r1', 'Imported', 'one two three', 3),
        ],
      },
    }),
  );
  await page.route(`**/api/scenes/${first.id}/prose/revisions/*/restore`, (route) => {
    restores.push(route.request().url().split('/').at(-2) ?? '');
    return route.fulfill({
      json: { version: cached.version + 1, body: 'one two three', previousRevisionId: 'r2' },
    });
  });
  return restores;
}

const open = async (page: Page) => {
  await page.getByTestId(writeTestIds.sceneMore(first.id)).click();
  await page.getByTestId(writeTestIds.sceneHistory(first.id)).click();
};

test('History_WhenOpened_ShouldListRowsMarkCurrentAndDisableRestoreOnIt', async ({
  mount,
  page,
}) => {
  await stubHistory(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await open(page);

  await expect(page.getByTestId(writeTestIds.anyHistoryRow)).toHaveCount(2);
  await expect(page.getByTestId(writeTestIds.historyCurrent)).toHaveCount(1);
  await expect(page.getByTestId(writeTestIds.historyRestore)).toBeDisabled();
});

test('History_WhenAnOlderRowIsRestored_ShouldCompareConfirmAndOfferUndo', async ({
  mount,
  page,
}) => {
  const restores = await stubHistory(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await open(page);

  await page.getByTestId(writeTestIds.historyRow('r1')).click();
  await expect(page.getByTestId(writeTestIds.historyAdded)).toHaveText('2');
  await expect(page.getByTestId(writeTestIds.historyRemoved)).toHaveText('0');
  await expect(page.getByTestId(writeTestIds.historyDelta)).toHaveText('−2 words');
  await page.getByTestId(writeTestIds.historyRestore).click();
  await expect(page.getByTestId(writeTestIds.historyConfirmText)).toContainText('14:02');
  await page.getByTestId(writeTestIds.historyConfirm).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Restored');
  expect(restores).toEqual(['r1']);
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect.poll(() => restores).toEqual(['r1', 'r2']);
});

test('History_WhenCancelled_ShouldNotRestoreAndEscShouldReturnFocus', async ({ mount, page }) => {
  const restores = await stubHistory(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await open(page);

  await page.getByTestId(writeTestIds.historyRow('r1')).click();
  await page.getByTestId(writeTestIds.historyRestore).click();
  await page.getByTestId(writeTestIds.historyCancel).click();
  expect(restores).toEqual([]);
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('Escape');

  await expect(page.getByTestId(writeTestIds.history)).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.sceneMore(first.id))).toBeFocused();
});

test('History_WhenTheSceneIsArchived_ShouldHideRestore', async ({ mount, page }) => {
  await stubHistory(page, true);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await open(page);

  await expect(page.getByTestId(writeTestIds.historyReadOnly)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.historyRestore)).toHaveCount(0);
});
