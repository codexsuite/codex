import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline } from '@codex/core/api/structure';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [preface, first] = act.chapters;
const [opening, empty] = first.scenes;
const guyon = opening.linkedEntries[0].id;

async function stubOutline(page: Page, body: Outline | null = outline) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) =>
    body === null
      ? route.fulfill({ status: 404, json: { error: 'Book not found.', code: 'not_found' } })
      : route.fulfill({ json: body }),
  );
}

async function clearStorage(page: Page) {
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
}

// A book with three acts, so the third starts collapsed.
function threeActs(): Outline {
  const clone: Outline = structuredClone(outline);
  const extra = (n: number) => ({
    id: `0199a000-0000-7000-8000-00000000f${n}`,
    title: `Act number ${n}`,
    position: `a${n}`,
    chapters: [
      {
        id: `0199a000-0000-7000-8000-00000000e${n}`,
        number: 10 + n,
        title: `Later chapter ${n}`,
        slug: `later-${n}`,
        position: 'a0',
        archived: false,
        archivedSceneCount: 0,
        scenes: [],
        opener: null,
      },
    ],
    opener: null,
  });
  clone.acts.push(extra(1), extra(2));
  return clone;
}

test('Card_ShouldRenderLabelsEntriesAndWordsFromOutline', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  const card = page.getByTestId(planTestIds.scene(opening.id));
  await expect(card.getByTestId(planTestIds.sceneNumber)).toHaveText('Scene 1');
  await expect(card).toContainText('hook');
  await expect(page.getByTestId(planTestIds.label(opening.id, opening.labels[0].id))).toContainText(
    'Flashback',
  );
  await expect(page.getByTestId(planTestIds.entry(opening.id, guyon))).toHaveText('Guyon');
  await expect(page.getByTestId(planTestIds.sceneWords(opening.id))).toHaveText('420 words');
  await expect(page.getByTestId(planTestIds.scenePending(opening.id))).toBeVisible();
  await expect(page.getByTestId(planTestIds.sceneWords(empty.id))).toHaveText('Not written');
  await expect(page.getByTestId(planTestIds.chapter(preface.id))).toContainText('Unnumbered');
});

test('Label_WhenColorIsUnknown_ShouldMarkItUnknown', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  await expect(
    page.getByTestId(planTestIds.label(opening.id, opening.labels[0].id)),
  ).toHaveAttribute('data-tone', 'unknown');
});

test('SceneHash_WhenSceneExists_ShouldFlashIt', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan#scene=1.2` } });

  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveClass(/plan-flash/);
  await expect(page.getByTestId(planTestIds.scene(empty.id))).not.toHaveClass(/plan-flash/);
});

test('SceneHash_WhenSceneUnknown_ShouldDoNothing', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan#scene=9.9` } });

  await expect(page.getByTestId(planTestIds.scene(opening.id))).toBeVisible();
  await expect(page.getByTestId(planTestIds.loadError)).toHaveCount(0);
  expect(await page.locator('.plan-flash').count()).toBe(0);
});

test('Act_WhenThird_ShouldStartCollapsedAndRememberToggle', async ({ mount, page }) => {
  const body = threeActs();
  await stubOutline(page, body);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  const third = body.acts[2];
  const laterChapter = page.getByTestId(planTestIds.chapter(third.chapters[0].id));
  await expect(page.getByTestId(planTestIds.act(third.id))).toHaveAttribute(
    'data-collapsed',
    'true',
  );
  await expect(laterChapter).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.chapter(first.id))).toBeVisible();

  await page.getByTestId(planTestIds.actToggle(third.id)).click();
  await expect(laterChapter).toBeVisible();
  const stored = await page.evaluate(
    (id) => window.localStorage.getItem(`codex.plan.collapsed.${id}`),
    bookId,
  );
  expect(JSON.parse(stored ?? '{}')).toEqual({ [third.id]: false });
});

test('ChapterHash_WhenActCollapsed_ShouldExpandIt', async ({ mount, page }) => {
  const body = threeActs();
  await stubOutline(page, body);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan#chapter=12` } });

  const chapter = page.getByTestId(planTestIds.chapter(body.acts[2].chapters[0].id));
  await expect(chapter).toBeVisible();
  await expect(chapter).toHaveClass(/plan-flash/);
});

test('ViewToggle_ShouldSwitchToOutlineAndRemember', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  await page.getByTestId(planTestIds.viewOutline).click();
  await expect(page.getByTestId(planTestIds.outline)).toBeVisible();
  await expect(page.getByTestId(planTestIds.board)).toHaveCount(0);
  await expect(page.getByTestId(planTestIds.outlineScene(opening.id))).toContainText('The hook.');
  expect(
    await page.evaluate((id) => window.localStorage.getItem(`codex.plan.view.${id}`), bookId),
  ).toBe('outline');
});

test('KbHash_ShouldHighlightLinkedScenesAndDimOthers', async ({ mount, page }) => {
  await stubOutline(page);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan#kb=${guyon}` } });

  await expect(page.getByTestId(planTestIds.scene(opening.id))).toHaveAttribute(
    'data-emphasis',
    'hit',
  );
  await expect(page.getByTestId(planTestIds.scene(empty.id))).toHaveAttribute(
    'data-emphasis',
    'dim',
  );
});

test('Plan_WhenOutlineMissing_ShouldShowErrorState', async ({ mount, page }) => {
  await stubOutline(page, null);
  await clearStorage(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  await expect(page.getByTestId(planTestIds.loadError)).toContainText('Book not found.');
});
