import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { planTestIds } from './testIds';

// Plan below the write role (#80): the board reads, nothing moves or changes.
const book = library.standalone[0];
const [act] = outline.acts;
const [, chapter] = act.chapters;
const [scene] = chapter.scenes;
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };

async function stubPlan(page: Page, role: string) {
  const writes: string[] = [];
  await stubShellApi(page, library);
  const header = headerFor(allBooks(library).find((b) => b.id === book.id)!);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({ json: { ...header, role, owner } }),
  );
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(/\/api\/(scenes|chapters|acts|labels|structure)\//, (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().url());
    return route.fulfill({ status: 403, json: { error: 'Read only.', code: 'forbidden' } });
  });
  return writes;
}

test('Plan_WhenViewOnly_ShouldReadWithNoMovesOrEdits', async ({ mount, page }) => {
  const writes = await stubPlan(page, 'view');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

  await expect(page.getByTestId(planTestIds.scene(scene.id))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveText('View only');
  await expect(page.getByTestId(planTestIds.sceneTitle(scene.id))).toHaveAttribute('readonly', '');
  await expect(page.getByTestId(planTestIds.actTitle(act.id))).toHaveAttribute('readonly', '');
  for (const id of [
    planTestIds.newAct,
    planTestIds.addAct,
    planTestIds.newChapter(act.id),
    planTestIds.addChapter(act.id),
    planTestIds.actRename(act.id),
    planTestIds.newScene(chapter.id),
    planTestIds.chapterMore(chapter.id),
    planTestIds.grip('scene', scene.id),
    planTestIds.grip('chapter', chapter.id),
    planTestIds.grip('act', act.id),
    planTestIds.sceneEntryAdd(scene.id),
    planTestIds.sceneLabelAdd(scene.id),
  ]) {
    await expect(page.getByTestId(id)).toHaveCount(0);
  }

  // The scene menu only opens the scene in Write.
  await page.getByTestId(planTestIds.scene(scene.id)).hover();
  await page.getByTestId(planTestIds.sceneMore(scene.id)).click();
  await expect(page.getByTestId(planTestIds.sceneOpenInWrite(scene.id))).toBeVisible();
  await expect(page.getByTestId(planTestIds.sceneArchive(scene.id))).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.sceneLabels(scene.id))).toHaveCount(0);

  // A double-click does not rename the chapter.
  await page.getByTestId(planTestIds.chapterTitle(chapter.id)).dblclick();
  await expect(page.getByTestId(planTestIds.chapterTitleInput(chapter.id))).toHaveCount(0);
  expect(writes).toEqual([]);
});

test('Plan_WhenWrite_ShouldKeepTheEditingControls', async ({ mount, page }) => {
  await stubPlan(page, 'write');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

  await expect(page.getByTestId(planTestIds.newAct)).toBeVisible();
  await expect(page.getByTestId(planTestIds.newScene(chapter.id))).toBeVisible();
  await expect(page.getByTestId(planTestIds.sceneTitle(scene.id))).not.toHaveAttribute(
    'readonly',
    '',
  );
});
