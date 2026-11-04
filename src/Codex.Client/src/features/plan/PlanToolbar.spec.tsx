import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import labels from '../../../../Codex.TestSupport/Fixtures/structure/labels.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline } from '@codex/core/api/structure';
import { editingTestIds } from '../../editing/testIds';
import { testIds } from '../../testIds';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [preface, first] = act.chapters;
const [opening, empty] = first.scenes;
const [flashback] = labels;
const [person] = kb.entries;

// Two more acts, so there is something for Collapse all to do (the third starts collapsed).
function threeActs(): Outline {
  const clone: Outline = structuredClone(outline);
  const extra = (n: number) => ({
    id: `0199a000-0000-7000-8000-00000000f${n}`,
    title: `Act number ${n}`,
    position: `a${n}`,
    chapters: [],
    opener: null,
  });
  clone.acts.push(extra(1), extra(2));
  return clone;
}

async function stubPlan(page: Page, body: Outline = outline) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) => route.fulfill({ json: body }));
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route(`**/api/books/${bookId}/labels`, (route) => route.fulfill({ json: labels }));
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
}

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

test('BookBar_OnPlan_ShouldShowTheScopeCounts', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await expect(page.getByTestId(testIds.shell.scopeSlot).getByTestId(planTestIds.stats)).toHaveText(
    '420 words · 2 pages · 2m read',
  );
});

test('Find_WhenTyped_ShouldDimScenesThatDoNotMatch', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await page.getByTestId(planTestIds.search).fill('hook');

  await expect(page.getByTestId(planTestIds.scene(opening.id))).toHaveAttribute(
    'data-emphasis',
    'none',
  );
  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveAttribute(
    'data-emphasis',
    'dim',
  );
  await page.getByTestId(planTestIds.search).fill('');
  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveAttribute(
    'data-emphasis',
    'none',
  );
});

test('LabelFilter_WhenLabelPicked_ShouldDimScenesWithoutIt', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await page.getByTestId(planTestIds.labelFilter).click();
  await page.getByTestId(planTestIds.labelOption(flashback.id)).click();

  await expect(page.getByTestId(planTestIds.labelFilter)).toContainText('Flashback');
  await expect(page.getByTestId(planTestIds.scene(opening.id))).toHaveAttribute(
    'data-emphasis',
    'none',
  );
  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveAttribute(
    'data-emphasis',
    'dim',
  );
  await page.getByTestId(planTestIds.labelFilter).click();
  await page.getByTestId(planTestIds.labelOption('any')).click();
  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveAttribute(
    'data-emphasis',
    'none',
  );
});

test('CollapseAll_ShouldCollapseEveryActAndThenOfferExpandAll', async ({ mount, page }) => {
  const body = threeActs();
  await stubPlan(page, body);
  await open(mount);
  const toggle = page.getByTestId(planTestIds.toggleAll);

  await expect(toggle).toHaveText('Collapse all');
  await toggle.click();
  for (const a of body.acts)
    await expect(page.getByTestId(planTestIds.act(a.id))).toHaveAttribute('data-collapsed', 'true');
  await expect(toggle).toHaveText('Expand all');

  await toggle.click();
  for (const a of body.acts)
    await expect(page.getByTestId(planTestIds.act(a.id))).toHaveAttribute(
      'data-collapsed',
      'false',
    );
});

test('CollapseAll_InOutlineView_ShouldNotBeOffered', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await page.getByTestId(planTestIds.viewOutline).click();

  await expect(page.getByTestId(planTestIds.toggleAll)).toHaveCount(0);
});

test('NewChapterSlot_ShouldAppendAChapterToTheAct', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/acts/${act.id}/chapters`, (route) =>
    route.fulfill({ status: 201, json: { ...preface, id: 'new-chapter' } }),
  );
  await open(mount);

  const request = page.waitForRequest(`**/api/acts/${act.id}/chapters`);
  await page.getByTestId(planTestIds.addChapter(act.id)).click();

  expect((await request).postDataJSON()).toEqual({ title: null, afterId: null });
});

test('Archived_InToolbar_ShouldCountTheBookAndRestoreFromTheList', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/chapters/${first.id}/archived-scenes`, (route) =>
    route.fulfill({
      json: [
        {
          id: 'archived-1',
          chapterNumber: 1,
          wasSceneNumber: 3,
          title: null,
          summary: 'Cut scene.',
          words: 0,
          useCount: 0,
        },
      ],
    }),
  );
  await open(mount);

  await expect(page.getByTestId(planTestIds.archivedToggle)).toHaveText('Archived (1)');
  await expect(page.getByTestId(planTestIds.archivedList)).toHaveCount(0);
  await page.getByTestId(planTestIds.archivedToggle).click();
  await expect(page.getByTestId(planTestIds.archivedScene('archived-1'))).toContainText(
    'Chapter 1 · was Scene 3',
  );
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(planTestIds.archivedList)).toHaveCount(0);
});

test('CardCodex_WhenEntryPicked_ShouldPostSceneLink', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${empty.id}/kb`, (route) =>
    route.fulfill({ status: 204, body: '' }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.sceneEntryAdd(empty.id)).click();
  const request = page.waitForRequest(`**/api/scenes/${empty.id}/kb`);
  await page.getByTestId(planTestIds.sceneEntryOption(person.id)).click();

  expect((await request).postDataJSON()).toEqual({ entryId: person.id });
});

test('CardLabel_WhenLabelChecked_ShouldPutTheLabels', async ({ mount, page }) => {
  await stubPlan(page);
  await page.route(`**/api/scenes/${empty.id}/labels`, (route) =>
    route.fulfill({ json: [flashback] }),
  );
  await open(mount);

  await page.getByTestId(planTestIds.sceneLabelAdd(empty.id)).click();
  const request = page.waitForRequest(`**/api/scenes/${empty.id}/labels`);
  await page.getByTestId(editingTestIds.labelOption(flashback.id)).click();

  expect((await request).postDataJSON()).toEqual({ labelIds: [flashback.id] });
});

test('SceneCard_ShouldNumberScenesWithinTheirChapter', async ({ mount, page }) => {
  await stubPlan(page);
  await open(mount);

  await expect(
    page.getByTestId(planTestIds.scene(empty.id)).getByTestId(planTestIds.sceneNumber),
  ).toHaveText('Scene 2');
  await expect(page.getByTestId(planTestIds.chapter(first.id))).toContainText(
    'Chapter 1 · 2 scenes · 420',
  );
});
