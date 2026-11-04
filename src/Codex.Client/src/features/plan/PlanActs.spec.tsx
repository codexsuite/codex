import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import labels from '../../../../Codex.TestSupport/Fixtures/structure/labels.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline, OutlineAct } from '@codex/core/api/structure';
import { editingTestIds } from '../../editing/testIds';
import { testIds } from '../../testIds';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [preface, first] = act.chapters;
const [opening] = first.scenes;

// The act New Act answers with: one chapter holding one empty scene, as the server makes it.
const newAct: OutlineAct = {
  id: '0199a000-0000-7000-8000-0000000000a2',
  title: '',
  position: 'a1',
  chapters: [
    {
      id: '0199a000-0000-7000-8000-0000000000c2',
      number: 2,
      title: 'New chapter',
      slug: 'new-chapter',
      position: 'a0',
      archived: false,
      archivedSceneCount: 0,
      scenes: [
        {
          id: '0199a000-0000-7000-8000-0000000000e3',
          number: '2.1',
          title: null,
          summary: null,
          status: 'outline',
          position: 'a0',
          words: 0,
          hasPendingDraft: false,
          labels: [],
          linkedEntries: [],
        },
      ],
      opener: null,
    },
  ],
  opener: null,
};

const withActs = (...acts: OutlineAct[]): Outline => ({
  ...outline,
  acts: [...outline.acts, ...acts],
});

// The outline route answers with whatever `current.outline` is when the page asks.
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

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

test('NewAct_ShouldAddTheActAndPutTheCaretInItsTitle', async ({ mount, page }) => {
  const current = { outline: outline as Outline };
  await stubPlan(page, current);
  await page.route(`**/api/books/${bookId}/acts`, (route) => {
    current.outline = withActs(newAct);
    return route.fulfill({ status: 201, json: newAct });
  });
  await open(mount);

  const created = page.waitForRequest(
    (r) => r.url().endsWith(`/api/books/${bookId}/acts`) && r.method() === 'POST',
  );
  await page.getByTestId(planTestIds.newAct).click();

  expect((await created).postDataJSON()).toEqual({ title: null, afterId: null });
  await expect(page.getByTestId(planTestIds.actTitle(newAct.id))).toBeFocused();
  await expect(page.getByTestId(planTestIds.actTitle(newAct.id))).toHaveAttribute(
    'placeholder',
    'Name this act',
  );
  await expect(page.getByTestId(planTestIds.scene(newAct.chapters[0].scenes[0].id))).toBeVisible();
});

test('AddAct_AtTheFootOfTheBoard_ShouldAddAnAct', async ({ mount, page }) => {
  const current = { outline: outline as Outline };
  await stubPlan(page, current);
  await page.route(`**/api/books/${bookId}/acts`, (route) => {
    current.outline = withActs(newAct);
    return route.fulfill({ status: 201, json: newAct });
  });
  await open(mount);

  await page.getByTestId(planTestIds.addAct).click();

  await expect(page.getByTestId(planTestIds.act(newAct.id))).toBeVisible();
});

test('ActTitle_WhenTypedAndEnterPressed_ShouldRenameTheAct', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/acts/${act.id}`, (route) =>
    route.fulfill({ json: { ...act, title: 'Beginnings' } }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.actRename(act.id)).click();
  const title = page.getByTestId(planTestIds.actTitle(act.id));
  await expect(title).toBeFocused();
  const renamed = page.waitForRequest(
    (r) => r.url().endsWith(`/api/acts/${act.id}`) && r.method() === 'PATCH',
  );
  await title.fill('Beginnings');
  await title.press('Enter');

  expect((await renamed).postDataJSON()).toEqual({ title: 'Beginnings' });
  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveText('Saved');
});

test('ActMenu_DeleteAct_ShouldArchiveItAndUndoRestores', async ({ mount, page }) => {
  const current = { outline: withActs(newAct) };
  await stubPlan(page, current);
  await page.route(`**/api/acts/${newAct.id}/archive`, (route) => {
    current.outline = outline as Outline;
    return route.fulfill({ json: { actId: newAct.id, archivedUses: [] } });
  });
  await page.route(`**/api/acts/${newAct.id}/restore`, (route) => {
    current.outline = withActs(newAct);
    return route.fulfill({ json: newAct });
  });
  await open(mount);

  await page.getByTestId(planTestIds.actMore(newAct.id)).click();
  await page.getByTestId(planTestIds.actDelete(newAct.id)).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Act deleted');
  await expect(page.getByTestId(planTestIds.act(newAct.id))).toHaveCount(0);
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect(page.getByTestId(planTestIds.act(newAct.id))).toBeVisible();
});

test('ActMenu_DeleteAct_WhenItIsTheLastAct_ShouldToastTheError', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await page.route(`**/api/acts/${act.id}/archive`, (route) =>
    route.fulfill({
      status: 409,
      json: { error: 'A book needs at least one act.', code: 'conflict' },
    }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.actMore(act.id)).click();
  await page.getByTestId(planTestIds.actDelete(act.id)).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Could not delete the act: A book needs at least one act.',
  );
  await expect(page.getByTestId(planTestIds.act(act.id))).toBeVisible();
});

test('ActMenu_CollapseAct_ShouldCollapseIt', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await open(mount);

  await page.getByTestId(planTestIds.actMore(act.id)).click();
  await page.getByTestId(planTestIds.actMenuToggle(act.id)).click();

  await expect(page.getByTestId(planTestIds.act(act.id))).toHaveAttribute('data-collapsed', 'true');
  await page.getByTestId(planTestIds.actMore(act.id)).click();
  await expect(page.getByTestId(planTestIds.actMenuToggle(act.id))).toHaveText('Expand act');
});

test('Scope_WhenAnActIsPicked_ShouldShowOnlyThatActAndItsCounts', async ({ mount, page }) => {
  await stubPlan(page, { outline: withActs(newAct) });
  await open(mount);

  await page.getByTestId(planTestIds.scope).selectOption(`act:${newAct.id}`);

  await expect(page.getByTestId(planTestIds.act(act.id))).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.act(newAct.id))).toContainText('Act 2');
  await expect(page.getByTestId(planTestIds.addAct)).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.scopeSlot).getByTestId(planTestIds.stats)).toHaveText(
    '0 words · 0 pages · 0m read',
  );
});

test('Scope_WhenAChapterIsPicked_ShouldShowOnlyThatChapter', async ({ mount, page }) => {
  await stubPlan(page, { outline: outline as Outline });
  await open(mount);

  await page.getByTestId(planTestIds.scope).selectOption(`chapter:${first.id}`);

  await expect(page.getByTestId(planTestIds.chapter(preface.id))).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.scene(opening.id))).toBeVisible();
  await expect(page.getByTestId(planTestIds.addChapter(act.id))).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.scopeSlot).getByTestId(planTestIds.stats)).toHaveText(
    '420 words · 2 pages · 2m read',
  );
  await page.getByTestId(planTestIds.viewOutline).click();
  await expect(page.getByTestId(planTestIds.outlineChapter(preface.id))).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.outlineChapter(first.id))).toBeVisible();
});
