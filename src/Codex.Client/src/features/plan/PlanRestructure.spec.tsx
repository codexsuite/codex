import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import labels from '../../../../Codex.TestSupport/Fixtures/structure/labels.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline } from '@codex/core/api/structure';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [preface, first] = outline.acts[0].chapters;
const [opening, second] = first.scenes;

async function stubPlan(page: Page, current: { outline: Outline }) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) =>
    route.fulfill({ json: current.outline }),
  );
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route(`**/api/books/${bookId}/labels`, (route) => route.fulfill({ json: labels }));
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
}

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>, hash = '') =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan${hash}` } });

test('ChapterTitle_WhenRenamedFromTheMenu_ShouldPatchTheChapter', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/chapters/${first.id}`, (route) =>
    route.fulfill({ json: { ...first, title: 'Renamed' } }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.chapterMore(first.id)).click({ force: true });
  await page.getByTestId(planTestIds.chapterRename(first.id)).click();
  const input = page.getByTestId(planTestIds.chapterTitleInput(first.id));
  await expect(input).toBeFocused();
  const patched = page.waitForRequest(
    (r) => r.url().endsWith(`/api/chapters/${first.id}`) && r.method() === 'PATCH',
  );
  await input.fill('Renamed');
  await input.press('Enter');

  expect((await patched).postDataJSON()).toEqual({ title: 'Renamed' });
});

test('ChapterTitle_WhenEmptied_ShouldShowAnInlineErrorAndSendNothing', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  let patches = 0;
  await page.route(`**/api/chapters/${first.id}`, (route) => {
    patches += 1;
    return route.fulfill({ json: first });
  });
  await open(mount);

  await page.getByTestId(planTestIds.chapterMore(first.id)).click({ force: true });
  await page.getByTestId(planTestIds.chapterRename(first.id)).click();
  const input = page.getByTestId(planTestIds.chapterTitleInput(first.id));
  await input.fill('  ');
  await input.press('Enter');

  await expect(page.getByTestId(planTestIds.chapterTitleError(first.id))).toHaveText(
    'The title cannot be empty.',
  );
  expect(patches).toBe(0);
});

test('ChapterMenu_DeleteChapter_ShouldArchiveItAndNameTheScenes', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/chapters/${first.id}/archive`, (route) =>
    route.fulfill({ json: { chapterId: first.id, sceneCount: 2, archivedUses: [] } }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.chapterMore(first.id)).click({ force: true });
  await page.getByTestId(planTestIds.chapterDelete(first.id)).click();

  await expect(page.getByText('Chapter archived · 2 scenes')).toBeVisible();
});

test('SceneMenu_MoveToChapter_ShouldPutTheSceneAtTheEndOfTheOtherChapter', async ({
  mount,
  page,
}) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/scenes/${opening.id}/position`, (route) =>
    route.fulfill({ json: outline }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.sceneMore(opening.id)).click({ force: true });
  await expect(page.getByTestId(planTestIds.sceneMerge(opening.id))).toHaveCount(0);
  await page.getByTestId(planTestIds.sceneMove(opening.id)).click();
  await expect(page.getByTestId(planTestIds.sceneMoveTarget(first.id))).toHaveCount(0);
  const moved = page.waitForRequest(
    (r) => r.url().endsWith(`/api/scenes/${opening.id}/position`) && r.method() === 'PUT',
  );
  await page.getByTestId(planTestIds.sceneMoveTarget(preface.id)).click();

  expect((await moved).postDataJSON()).toEqual({ chapterId: preface.id, afterId: null });
});

test('SceneMenu_MergeIntoPrevious_ShouldPostForTheLaterScene', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/scenes/${second.id}/merge-previous`, (route) =>
    route.fulfill({ json: { sceneId: opening.id, version: 2, archivedSceneId: second.id } }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.sceneMore(second.id)).click({ force: true });
  const merged = page.waitForRequest(
    (r) => r.url().endsWith(`/api/scenes/${second.id}/merge-previous`) && r.method() === 'POST',
  );
  await page.getByTestId(planTestIds.sceneMerge(second.id)).click();

  await merged;
  await expect(page.getByText('Scenes merged')).toBeVisible();
});

test('ArchivedList_WhenOpenedFromAnArchivedChapterLink_ShouldShowThatChapterWithRestore', async ({
  mount,
  page,
}) => {
  const archivedChapter = {
    ...preface,
    id: '0199a000-0000-7000-8000-0000000000c9',
    title: 'The Cut',
    archived: true,
    number: null,
    archivedSceneCount: 0,
    scenes: [],
  };
  const current = {
    outline: {
      ...outline,
      acts: [{ ...outline.acts[0], chapters: [...outline.acts[0].chapters, archivedChapter] }],
    } as Outline,
  };
  await stubPlan(page, current);
  await open(mount, `#archived=${archivedChapter.id}`);

  await expect(page.getByTestId(planTestIds.archivedList)).toBeVisible();
  await expect(page.getByTestId(planTestIds.archivedChapter(archivedChapter.id))).toContainText(
    'The Cut',
  );
  await expect(page.getByTestId(planTestIds.restoreChapter(archivedChapter.id))).toBeVisible();
});
