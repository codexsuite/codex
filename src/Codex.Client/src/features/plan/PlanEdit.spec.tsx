import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline, OutlineScene } from '@codex/core/api/structure';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { entryDragType } from '../../shell/dragPayload';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [, first] = outline.acts[0].chapters;
const [opening, empty] = first.scenes;
const [person] = kb.entries;

async function stubPlan(page: Page) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
}

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

const saved = (scene: { id: string }, summary: string): OutlineScene => ({
  ...(first.scenes.find((s) => s.id === scene.id) as OutlineScene),
  summary,
});

test('Card_WhenEntryDropped_ShouldPostSceneLink', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${empty.id}/kb`, (route) =>
    route.fulfill({ status: 204, body: '' }),
  );
  await open(mount);

  const dataTransfer = await page.evaluateHandle(
    ([type, entryId, name]) => {
      const dt = new DataTransfer();
      dt.setData(type, JSON.stringify({ entryId, name }));
      return dt;
    },
    [entryDragType, person.id, person.name] as const,
  );
  const request = page.waitForRequest(`**/api/scenes/${empty.id}/kb`);
  await page.getByTestId(planTestIds.scene(empty.id)).dispatchEvent('drop', { dataTransfer });

  expect((await request).postDataJSON()).toEqual({ entryId: person.id });
});

test('Summary_WhenMentionTyped_ShouldPatchWithTheLink', async ({ mount, page }) => {
  await stubPlan(page);
  let body: unknown = null;
  await page.route(`**/api/scenes/${opening.id}`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ json: saved(opening, 'x') });
  });
  await open(mount);

  const editor = page
    .getByTestId(planTestIds.scene(opening.id))
    .getByTestId(proseTestIds.summaryEditor);
  await editor.click();
  await page.keyboard.press('End');
  await editor.pressSequentially(` @${person.name.slice(0, 3)}`);
  await expect(page.getByTestId(proseTestIds.mentionOption(person.id))).toBeVisible();
  await page.keyboard.press('Enter');
  const request = page.waitForRequest(
    (r) => r.method() === 'PATCH' && r.url().endsWith(`/api/scenes/${opening.id}`),
  );
  await page.getByTestId(planTestIds.page).click({ position: { x: 2, y: 2 } });
  await request;

  expect(body).toMatchObject({ title: null, status: null });
  expect(JSON.stringify(body)).toContain(`(kb:${person.id})`);
});

test('Autosave_WhenServerFails_ShouldShowUnsaved', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${opening.id}`, (route) =>
    route.fulfill({ status: 500, json: { error: 'Boom.', code: 'internal_error' } }),
  );
  await open(mount);

  const editor = page
    .getByTestId(planTestIds.scene(opening.id))
    .getByTestId(proseTestIds.summaryEditor);
  await editor.click();
  await page.keyboard.press('End');
  await editor.pressSequentially(' more');
  await page.getByTestId(planTestIds.page).click({ position: { x: 2, y: 2 } });

  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute(
    'data-state',
    'unsaved',
  );
});

test('NewScene_WhenAdded_ShouldFocusItsSummary', async ({ mount, page }) => {
  await stubPlan(page);
  const created: OutlineScene = {
    id: '0199a000-0000-7000-8000-0000000fffff',
    number: '1.3',
    title: null,
    summary: null,
    status: 'outline',
    position: 'a2',
    words: 0,
    hasPendingDraft: false,
    labels: [],
    linkedEntries: [],
  };
  const after: Outline = structuredClone(outline);
  after.acts[0].chapters[1].scenes.push(created);
  await page.route(`**/api/chapters/${first.id}/scenes`, async (route) => {
    await page.unroute(`**/api/books/${bookId}/outline`);
    await page.route(`**/api/books/${bookId}/outline`, (r) => r.fulfill({ json: after }));
    await route.fulfill({ status: 201, json: created });
  });
  await open(mount);

  await page.getByTestId(planTestIds.newScene(first.id)).click();

  await expect(page.getByTestId(planTestIds.scene(created.id))).toBeVisible();
  await expect(
    page.getByTestId(planTestIds.scene(created.id)).getByTestId(proseTestIds.summaryEditor),
  ).toBeFocused();
});

test('SceneMenu_ShouldLinkToTheSceneInWrite', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await page.getByTestId(planTestIds.scene(opening.id)).hover();
  await page.getByTestId(planTestIds.sceneMore(opening.id)).click();

  await expect(page.getByTestId(planTestIds.sceneOpenInWrite(opening.id))).toHaveAttribute(
    'href',
    `/books/${bookId}/write#scene=${opening.number}`,
  );
});
