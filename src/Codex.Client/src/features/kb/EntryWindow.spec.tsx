import { expect, test } from '@playwright/experimental-ct-react';
import type { MountResult } from '@playwright/experimental-ct-react';
import type { ReactElement } from 'react';
import type { Page } from 'playwright';
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { HashProbe } from '../../test-support/HashProbe';
import { stubShellApi } from '../../test-support/shellStubs';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose';
import { testIds } from '../../testIds';
import { kbTestIds } from './testIds';

const hymns = library.series[0].books[1].id;
const [ward, harbour] = kb.entries;
const people = kb.categories[0];
const wardEntry = { ...entry, id: ward.id, name: ward.name, categoryId: people.id };

type Mount = (component: ReactElement, options?: object) => Promise<MountResult>;

async function openWindow(
  mount: Mount,
  page: Page,
  hash = `#kb=${ward.id}&entry=${ward.id}`,
  book: typeof kb = kb,
) {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: book }));
  await page.route('**/api/kb/entries/*', (route) => {
    if (route.request().method() === 'PATCH') {
      return route.fulfill({
        json: { ...wardEntry, ...(route.request().postDataJSON() as object) },
      });
    }
    return route.fulfill({ json: wardEntry });
  });
  await mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${hymns}/plan${hash}` } },
  );
  if (hash.includes('entry=')) {
    await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
  }
}

test('EntryWindow_UsedInTab_ShouldListScenesWithLinksToWrite', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.tab('used-in')).click();
  const [first] = entry.usedIn;
  await expect(page.getByTestId(kbTestIds.useRow(first.sceneId))).toContainText('Scene 4.1');
  await expect(page.getByTestId(kbTestIds.useKind(first.sceneId, 'timeline'))).toHaveText(
    'Timeline',
  );
  await expect(page.getByTestId(kbTestIds.useSnippet(first.sceneId))).toHaveText(first.snippet);
  await expect(page.getByTestId(kbTestIds.openInWrite(first.sceneId))).toHaveAttribute(
    'href',
    `/books/${first.bookId}/write#scene=4.1`,
  );
  await expect(page.getByTestId(kbTestIds.highlightInWrite)).toHaveAttribute(
    'href',
    `/books/${hymns}/write#kb=${ward.id}`,
  );
});

test('EntryWindow_TimelineTab_ShouldListNotesInBookOrder', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.tab('timeline')).click();
  await expect(page.getByTestId(kbTestIds.timelineNote(entry.timeline[0].id))).toContainText(
    entry.timeline[0].body,
  );
});

test('EntryWindow_WhenAliasCollides_ShouldShowTheServerMessage', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*', (route) =>
    route.request().method() === 'PATCH'
      ? route.fulfill({
          status: 409,
          json: { error: '"Harbour" is already a name or alias of Harbour.', code: 'conflict' },
        })
      : route.fulfill({ json: wardEntry }),
  );
  await page.getByTestId(kbTestIds.addAlias).click();
  await page.getByTestId(kbTestIds.aliasInput).fill('Harbour');
  await page.getByTestId(kbTestIds.aliasInput).press('Enter');
  await expect(page.getByTestId(kbTestIds.aliasError)).toContainText('already a name or alias');
  await expect(page.getByTestId(kbTestIds.alias('Harbour'))).toHaveCount(0);
});

test('EntryWindow_Edit_ShouldAutosaveOnBlur', async ({ mount, page }) => {
  await openWindow(mount, page);
  const patch = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(kbTestIds.summary).fill('A grim captain.');
  await page.getByTestId(kbTestIds.entryName).focus();
  const body = (await patch).postDataJSON() as { summary: string; name: string };
  expect(body.summary).toBe('A grim captain.');
  expect(body.name).toBe(ward.name);
});

test('EntryWindow_AltArrow_ShouldMoveTheEntryWithinItsCategory', async ({ mount, page }) => {
  const mate = { ...ward, id: harbour.id.replace(/2$/, '9'), name: 'Mate' };
  await openWindow(mount, page, `#entry=${ward.id}`, { ...kb, entries: [ward, mate, harbour] });
  let body: unknown = null;
  await page.route('**/api/kb/entries/*/position', (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({ status: 204 });
  });
  await page.keyboard.press('Alt+ArrowDown');
  await expect.poll(() => body).toEqual({ afterId: mate.id });
});

test('EntryWindow_NewEntry_WhenClosedEmpty_ShouldWriteNothing', async ({ mount, page }) => {
  await openWindow(mount, page, '');
  let posted = false;
  await page.route('**/api/books/*/kb/entries', (route) => {
    posted = true;
    return route.fulfill({ status: 201, json: wardEntry });
  });
  await page.getByTestId(kbTestIds.newEntry(kb.categories[1].id)).click();
  await expect(page.getByTestId(kbTestIds.draftName)).toBeFocused();
  await page.getByTestId(kbTestIds.entryWindowClose).click();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toHaveCount(0);
  expect(posted).toBe(false);
});

test('EntryWindow_NewEntry_WhenNamed_ShouldCreateAndOpenIt', async ({ mount, page }) => {
  await openWindow(mount, page, '');
  const created = page.waitForRequest(
    (r) => r.method() === 'POST' && r.url().endsWith('/kb/entries'),
  );
  await page.route('**/api/books/*/kb/entries', (route) =>
    route.fulfill({ status: 201, json: { ...wardEntry, id: harbour.id, name: 'Lighthouse' } }),
  );
  await page.getByTestId(kbTestIds.newEntry(kb.categories[1].id)).click();
  await page.getByTestId(kbTestIds.draftName).fill('Lighthouse');
  await page.getByTestId(kbTestIds.draftName).press('Enter');
  expect((await created).postDataJSON()).toEqual({
    categoryId: kb.categories[1].id,
    name: 'Lighthouse',
  });
  await expect(page.getByTestId(testIds.shell.kbItem(harbour.id))).toBeVisible();
  await expect(page.getByTestId('test-hash-probe')).toHaveText(
    `#kb=${harbour.id}&entry=${harbour.id}`,
  );
});

test('EntryWindow_NewEntry_WhenClosedWhileCreating_ShouldNotOpenTheEntry', async ({
  mount,
  page,
}) => {
  await openWindow(mount, page, '');
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/books/*/kb/entries', async (route) => {
    await gate;
    await route.fulfill({
      status: 201,
      json: { ...wardEntry, id: harbour.id, name: 'Lighthouse' },
    });
  });
  await page.getByTestId(kbTestIds.newEntry(kb.categories[1].id)).click();
  await page.getByTestId(kbTestIds.draftName).fill('Lighthouse');
  await page.getByTestId(kbTestIds.draftName).press('Enter');
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(kbTestIds.entryWindow)).toHaveCount(0);
  const refreshed = page.waitForResponse(
    (r) => r.request().method() === 'GET' && r.url().endsWith('/api/books/' + hymns + '/kb'),
  );
  release();
  await refreshed;
  await page.evaluate(
    () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
  );
  await expect(page.getByTestId('test-hash-probe')).toHaveText('');
});

test('EntryWindow_NewCategory_ShouldPostNameAndColour', async ({ mount, page }) => {
  await openWindow(mount, page);
  const created = page.waitForRequest(
    (r) => r.method() === 'POST' && r.url().endsWith('/kb/categories'),
  );
  await page.route('**/api/books/*/kb/categories', (route) =>
    route.fulfill({
      status: 201,
      json: { ...kb.categories[1], id: kb.categories[2].id, name: 'Ships' },
    }),
  );
  await page.getByTestId(kbTestIds.categoryChip).click();
  await page.getByTestId(kbTestIds.newCategoryOpen).click();
  await page.getByTestId(kbTestIds.newCategoryName).fill('Ships');
  await page.getByTestId(kbTestIds.newCategoryColour('category-story')).click();
  await page.getByTestId(kbTestIds.newCategoryCreate).click();
  expect((await created).postDataJSON()).toEqual({ name: 'Ships', colour: 'category-story' });
});

test('EntryWindow_PreviousNext_ShouldStepThroughTheSidebarOrderAndKeepTheTab', async ({
  mount,
  page,
}) => {
  await openWindow(mount, page);
  await expect(page.getByTestId(kbTestIds.entryPrevious)).toBeDisabled();
  await page.getByTestId(kbTestIds.tab('used-in')).click();
  await page.route('**/api/kb/entries/*', (route) =>
    route.fulfill({ json: { ...wardEntry, id: harbour.id, name: harbour.name } }),
  );
  await page.getByTestId(kbTestIds.entryNext).click();
  await expect(page.getByTestId('test-hash-probe')).toHaveText(
    `#kb=${harbour.id}&entry=${harbour.id}`,
  );
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(harbour.name);
  await expect(page.getByTestId(kbTestIds.panel('used-in'))).toBeVisible();
  await expect(page.getByTestId(kbTestIds.entryNext)).toBeDisabled();
});

test('EntryWindow_Tabs_ShouldShowTheUseAndTimelineCounts', async ({ mount, page }) => {
  await openWindow(mount, page);
  await expect(page.getByTestId(kbTestIds.tabCount('used-in'))).toHaveText(
    String(entry.usedIn.length),
  );
  await expect(page.getByTestId(kbTestIds.tabCount('timeline'))).toHaveText(
    String(entry.timeline.length),
  );
  await expect(page.getByTestId(kbTestIds.tabCount('entry'))).toHaveCount(0);
});

test('EntryWindow_EntryTab_ShouldShowTheWordCountAndFormatTheContent', async ({ mount, page }) => {
  await openWindow(mount, page);
  await expect(page.getByTestId(kbTestIds.wordCount)).toHaveText('11 words');
  await page.getByTestId(kbTestIds.body).getByRole('textbox').click();
  const patch = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(kbTestIds.format('quote')).click();
  await page.getByTestId(kbTestIds.entryName).focus();
  expect(((await patch).postDataJSON() as { body: string }).body).toMatch(/^> /);
});

test('EntryWindow_MentionButton_ShouldOpenTheEntryPicker', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.body).getByRole('textbox').click();
  await page.getByTestId(kbTestIds.format('mention')).click();
  await expect(page.getByTestId(proseTestIds.mentionPicker)).toBeVisible();
});

test('EntryWindow_Window_ShouldFillTheMainAreaWithAMarginAndNoSaveStatus', async ({
  mount,
  page,
}) => {
  await openWindow(mount, page);
  const window = page.getByTestId(kbTestIds.entryWindow);
  const box = await window.boundingBox();
  const viewport = page.viewportSize();
  expect(box && viewport && viewport.height - (box.y + box.height)).toBe(16);
  await expect(window.getByTestId(editingTestIds.saveStatus)).toHaveCount(0);
});

test('EntryWindow_NewEntryButton_ShouldStartADraftInTheFirstBookCategory', async ({
  mount,
  page,
}) => {
  await openWindow(mount, page, '');
  await page.getByTestId(kbTestIds.newEntryButton).click();
  await expect(page.getByTestId(kbTestIds.draftName)).toBeFocused();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toContainText('New entry in Places');
});
