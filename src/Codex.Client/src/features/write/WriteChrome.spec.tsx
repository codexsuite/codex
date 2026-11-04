import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { proseTestIds } from '../../prose/testIds';
import { testIds } from '../../testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { shellTestIds } from '../../shell/shellTestIds';
import { formatStorageKey } from './formatStore';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const secondScene = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes)[1];
const [preface, chapterOne] = outline.acts.flatMap((a) => a.chapters);

async function openWrite(
  page: Page,
  mount: (component: React.JSX.Element, options: object) => unknown,
) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
}

const stored = (page: Page) =>
  page.evaluate((key) => window.localStorage.getItem(key), formatStorageKey(book.id));

test('Format_WhenToggled_ShouldStoreTheShapeAndApplyIt', async ({ mount, page }) => {
  await openWrite(page, mount);
  const beat = page.getByTestId(proseTestIds.directiveCard('beat'));
  await expect(beat).toBeVisible();

  await page.getByTestId(writeTestIds.formatButton).click();
  await page.getByTestId(writeTestIds.formatOption('width', 'wide')).click();
  await page.getByTestId(writeTestIds.formatOption('paragraphs', 'indent')).click();
  await page.getByTestId(writeTestIds.formatOption('beats', 'hide')).click();
  await page.getByTestId(writeTestIds.formatOption('summaries', 'hide')).click();
  await page.getByTestId(writeTestIds.formatOption('names', 'plain')).click();

  expect(JSON.parse((await stored(page)) ?? 'null')).toEqual({
    width: 'wide',
    paragraphs: 'indent',
    hideBeats: true,
    hideSummaries: true,
    underlineNames: false,
  });
  const doc = page.getByTestId(writeTestIds.manuscript);
  await expect(doc).toHaveAttribute('data-width', 'wide');
  await expect(doc).toHaveAttribute('data-hide-summaries', 'true');
  await expect(beat).toBeHidden();
});

test('Format_WhenStoredValueIsCorrupt_ShouldRenderDefaults', async ({ mount, page }) => {
  await page.addInitScript(
    (key) => window.localStorage.setItem(key, '{not json'),
    formatStorageKey(book.id),
  );
  await openWrite(page, mount);

  const doc = page.getByTestId(writeTestIds.manuscript);
  await expect(doc).toHaveAttribute('data-width', 'medium');
  await expect(doc).toHaveAttribute('data-hide-beats', 'false');
  await expect(doc).toHaveAttribute('data-underline-names', 'true');
  await expect(page.getByTestId(writeTestIds.loadError)).toHaveCount(0);
});

test('Format_WhenStoredShapeIsWrong_ShouldRenderDefaults', async ({ mount, page }) => {
  await page.addInitScript(
    (key) => window.localStorage.setItem(key, JSON.stringify({ width: 'huge' })),
    formatStorageKey(book.id),
  );
  await openWrite(page, mount);

  await expect(page.getByTestId(writeTestIds.manuscript)).toHaveAttribute('data-width', 'medium');
});

test('Scope_WhenSceneChosen_ShouldNarrowTheCounts', async ({ mount, page }) => {
  await openWrite(page, mount);
  const counts = page.getByTestId(writeTestIds.scopeCounts);
  await expect(counts).toHaveText('420 words · 2 pages · 2m read');

  await page.getByTestId(writeTestIds.scopeSelect).selectOption(`scene:${secondScene.id}`);

  await expect(counts).toHaveText('0 words · 0 pages · 0m read');
  await expect(page.getByTestId(/^write-scene-[0-9a-f-]+$/)).toHaveCount(1);
});

test('Focus_WhenEscPressed_ShouldLeaveAndReturnToTheManuscript', async ({ mount, page }) => {
  await openWrite(page, mount);
  const button = page.getByTestId(writeTestIds.focusButton);

  await button.click();
  await expect(button).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('body')).toHaveClass(/write-focus/);
  await page.keyboard.press('Escape');

  await expect(button).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('body')).not.toHaveClass(/write-focus/);
  await expect(page.getByTestId(writeTestIds.manuscript)).toBeFocused();
});

test('Focus_WhenFormatPopoverOpen_ShouldCloseThePopoverFirst', async ({ mount, page }) => {
  await openWrite(page, mount);
  await page.getByTestId(writeTestIds.focusButton).click();
  await page.getByTestId(writeTestIds.formatButton).click();

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(writeTestIds.formatPopover)).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.focusButton)).toHaveAttribute('aria-pressed', 'true');
});

test('Focus_WhenLeavingWriteWhileFocused_ShouldRemoveTheBodyClass', async ({ mount, page }) => {
  await openWrite(page, mount);
  await page.getByTestId(writeTestIds.focusButton).click();
  await expect(page.locator('body')).toHaveClass(/write-focus/);

  await page.keyboard.press('Escape');
  await page.getByTestId(writeTestIds.focusButton).click();
  await page.getByTestId(testIds.shell.tabPlan).click();

  await expect(page.getByTestId(writeTestIds.page)).toHaveCount(0);
  await expect(page.locator('body')).not.toHaveClass(/write-focus/);
});

test('Toolbar_ShouldShowTheChapterAndPositionBeingRead', async ({ mount, page }) => {
  await openWrite(page, mount);

  await expect(page.getByTestId(writeTestIds.chapterJump)).toHaveValue(chapterOne.id);
  await expect(page.getByTestId(writeTestIds.crumb)).toHaveText('Act 1 · Scene 1 of 2');
  await expect(page.getByTestId(writeTestIds.chapterNext)).toBeDisabled();
  await expect(page.getByTestId(writeTestIds.chapterPrevious)).toBeEnabled();
});

test('Find_WhenTyped_ShouldCountProseMatchesAndStepThroughThem', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({
      json: {
        ...manuscript,
        scenes: [
          manuscript.scenes[0],
          { ...manuscript.scenes[1], body: 'The yard was empty.\n', version: 1 },
        ],
      },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  const find = page.getByTestId(writeTestIds.find);
  const count = page.getByTestId(writeTestIds.findCount);

  await find.fill('YARD');
  await expect(count).toHaveText('2');
  await find.press('Enter');
  await expect(count).toHaveText('1 of 2');
  await find.press('Enter');
  await expect(count).toHaveText('2 of 2');
  await find.press('Shift+Enter');
  await expect(count).toHaveText('1 of 2');
  expect(
    await page.evaluate(() => [...(CSS.highlights.get('write-find') ?? [])].map(String)),
  ).toEqual(['yard', 'yard']);

  await find.press('Escape');
  await expect(count).toHaveCount(0);
});

test('ChapterMenu_WhenShowOnlyChosen_ShouldScopeTheManuscriptToTheChapter', async ({
  mount,
  page,
}) => {
  await openWrite(page, mount);

  await page.getByTestId(writeTestIds.chapterMore(chapterOne.id)).click();
  await page.getByTestId(writeTestIds.chapterScope(chapterOne.id)).click();

  await expect(page.getByTestId(writeTestIds.scopeSelect)).toHaveValue(`chapter:${chapterOne.id}`);
  await expect(page.getByTestId(writeTestIds.chapter(preface.id))).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.chapterMenu(chapterOne.id))).toHaveCount(0);
});

test('SidebarToggle_WhenClicked_ShouldHideAndShowTheKnowledgeBase', async ({ mount, page }) => {
  await openWrite(page, mount);
  const toggle = page.getByTestId(shellTestIds.sidebarToggle);
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();

  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toHaveCount(0);
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await toggle.click();
  await expect(page.getByTestId(testIds.shell.kbSidebar)).toBeVisible();
});
