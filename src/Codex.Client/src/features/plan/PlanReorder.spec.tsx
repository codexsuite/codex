import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { ArchivedScene } from '@codex/core/api/plan';
import type { Outline } from '@codex/core/api/structure';
import { editingTestIds } from '../../editing/testIds';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [preface, first] = act.chapters;
const [opening, second] = first.scenes;

async function stubPlan(page: Page) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) => route.fulfill({ json: outline }));
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
}

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

// The outline the server would answer with after `second` moves above `opening`.
function swapped(): Outline {
  const clone: Outline = structuredClone(outline);
  const scenes = clone.acts[0].chapters[1].scenes.reverse();
  scenes.forEach((scene, i) => (scene.number = `1.${i + 1}`));
  return clone;
}

test('Scene_WhenAltDown_ShouldMoveOneStepAndKeepFocus', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${opening.id}/position`, (route) =>
    route.fulfill({ json: swapped() }),
  );
  await open(mount);
  const grip = page.getByTestId(planTestIds.grip('scene', opening.id));
  await grip.focus();

  const request = page.waitForRequest(`**/api/scenes/${opening.id}/position`);
  await page.keyboard.press('Alt+ArrowDown');

  expect((await request).postDataJSON()).toEqual({ chapterId: first.id, afterId: second.id });
  await expect(
    page.getByTestId(planTestIds.scene(opening.id)).getByTestId(planTestIds.sceneNumber),
  ).toHaveText('Scene 2');
  await expect(
    page.getByTestId(planTestIds.scene(second.id)).getByTestId(planTestIds.sceneNumber),
  ).toHaveText('Scene 1');
  await expect(grip).toBeFocused();
});

test('Chapter_WhenAltLeft_ShouldAskForTheFirstSlotOfItsAct', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/chapters/${first.id}/position`, (route) =>
    route.fulfill({ json: outline }),
  );
  await open(mount);
  await page.getByTestId(planTestIds.grip('chapter', first.id)).focus();

  const request = page.waitForRequest(`**/api/chapters/${first.id}/position`);
  await page.keyboard.press('Alt+ArrowLeft');

  expect((await request).postDataJSON()).toEqual({ actId: act.id, afterId: null });
});

test('Move_WhenServerFails_ShouldRevertAndSayWhy', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${opening.id}/position`, (route) =>
    route.fulfill({
      status: 500,
      json: { error: 'Something went wrong on the server.', code: 'internal_error' },
    }),
  );
  await open(mount);
  await page.getByTestId(planTestIds.grip('scene', opening.id)).focus();

  await page.keyboard.press('Alt+ArrowDown');

  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Could not move the scene');
  await expect(
    page.getByTestId(planTestIds.scene(opening.id)).getByTestId(planTestIds.sceneNumber),
  ).toHaveText('Scene 1');
  await expect(
    page.getByTestId(planTestIds.scene(second.id)).getByTestId(planTestIds.sceneNumber),
  ).toHaveText('Scene 2');
});

test('Archive_ShouldNameTheUsesAndUndoRestores', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${opening.id}/archive`, (route) =>
    route.fulfill({
      json: {
        sceneId: opening.id,
        archivedUses: [
          { useId: preface.id, sourceId: second.id, sourceTitle: 'The Velocity Trap' },
        ],
      },
    }),
  );
  await page.route(`**/api/scenes/${opening.id}/restore`, (route) =>
    route.fulfill({ json: { sceneId: opening.id, restoredUses: [] } }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.scene(opening.id)).hover();
  await page.getByTestId(planTestIds.sceneMore(opening.id)).click();
  await page.getByTestId(planTestIds.sceneArchive(opening.id)).click();

  const toast = page.getByTestId(editingTestIds.toast);
  await expect(toast).toHaveText(/Scene archived · 1 source use archived \(The Velocity Trap\)/);
  const restore = page.waitForRequest(`**/api/scenes/${opening.id}/restore`);
  await page.getByTestId(editingTestIds.toastUndo).click();
  await restore;
});

test('Archive_WhenSceneAlreadyArchived_ShouldToastTheError', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${opening.id}/archive`, (route) =>
    route.fulfill({
      status: 409,
      json: { error: 'The scene is already archived.', code: 'conflict' },
    }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.scene(opening.id)).hover();
  await page.getByTestId(planTestIds.sceneMore(opening.id)).click();
  await page.getByTestId(planTestIds.sceneArchive(opening.id)).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText('already archived');
});

test('ArchivedList_ShouldOfferRestore', async ({ mount, page }) => {
  await stubPlan(page);
  const archived: ArchivedScene = {
    id: second.id,
    chapterNumber: 1,
    wasSceneNumber: 2,
    title: null,
    summary: 'Guyon leaves.',
    words: 0,
    useCount: 2,
  };
  await page.route(`**/api/chapters/${first.id}/archived-scenes`, (route) =>
    route.fulfill({ json: [archived] }),
  );
  await page.route(`**/api/scenes/${second.id}/restore`, (route) =>
    route.fulfill({ json: { sceneId: second.id, restoredUses: [] } }),
  );
  await open(mount);

  await expect(page.getByTestId(planTestIds.archivedToggle)).toHaveText('Archived (1)');
  await page.getByTestId(planTestIds.archivedToggle).click();
  const row = page.getByTestId(planTestIds.archivedScene(second.id));
  await expect(row).toContainText('Chapter 1 · was Scene 2');
  await expect(row).toContainText('2 source uses archived with it');
  const restore = page.waitForRequest(`**/api/scenes/${second.id}/restore`);
  await page.getByTestId(planTestIds.restore(second.id)).click();
  await restore;
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Scene restored');
});
