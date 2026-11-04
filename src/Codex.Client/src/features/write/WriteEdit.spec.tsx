import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { testIds } from '../../testIds';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first, second] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);

interface Put {
  sceneId: string;
  body: string;
  version: number;
}

type Answer = { status: number; json: unknown };
type Reply = (put: Put) => Answer | Promise<Answer>;

// Stubs the screen's reads and answers every prose PUT with `reply`; returns the PUTs the page sent.
async function stubWrite(page: Page, reply: Reply) {
  const puts: Put[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route('**/api/scenes/*/prose', async (route) => {
    const request = route.request();
    if (request.method() === 'PUT') {
      const put = {
        sceneId: request.url().split('/').at(-2) ?? '',
        ...(request.postDataJSON() as { body: string; version: number }),
      };
      puts.push(put);
      return route.fulfill(await reply(put));
    }
    const sceneId = request.url().split('/').at(-2);
    return route.fulfill({
      json: { sceneId, body: 'Server copy.\n', version: 9, updatedAt: '2026-01-01T00:00:00Z' },
    });
  });
  return puts;
}

const editor = (page: Page, sceneId: string) =>
  page.getByTestId(writeTestIds.scene(sceneId)).getByTestId(proseTestIds.editor);
const status = (page: Page) => page.getByTestId(editingTestIds.saveStatus);

test('Edit_WhenBlurred_ShouldSaveWithTheLoadedVersionThenTheNewOne', async ({ mount, page }) => {
  const puts = await stubWrite(page, (put) => ({
    status: 200,
    json: { version: put.version + 1 },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await editor(page, first.id).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' More.');
  await expect(status(page)).toHaveAttribute('data-state', 'unsaved');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  expect(puts).toHaveLength(1);
  expect(puts[0].version).toBe(4);
  expect(puts[0].body).toContain('More.');

  await editor(page, first.id).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' Again.');
  await page.getByTestId(writeTestIds.manuscript).focus();
  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  expect(puts).toHaveLength(2);
  expect(puts[1].version).toBe(5);
});

test('Save_WhenOneSceneFailsAndAnotherSaves_ShouldShowTheWorstState', async ({ mount, page }) => {
  await stubWrite(page, (put) =>
    put.sceneId === first.id
      ? { status: 503, json: { error: 'Down.', code: 'unavailable' } }
      : { status: 200, json: { version: put.version + 1 } },
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await editor(page, first.id).click();
  await page.keyboard.type('Lost? ');
  await editor(page, second.id).click();
  await expect(status(page)).toHaveAttribute('data-state', 'unsaved');
  await page.keyboard.type('Second scene.');
  await page.getByTestId(writeTestIds.manuscript).focus();

  // The second scene saved; the first is still waiting, and the bar says so.
  await expect(status(page)).toHaveAttribute('data-state', 'unsaved');
  await expect(status(page)).toHaveText('Unsaved · Retry');
});

test('Save_WhenVersionIsStale_ShouldShowTheBannerAndReloadTheServerCopy', async ({
  mount,
  page,
}) => {
  await stubWrite(page, () => ({
    status: 409,
    json: { error: 'Updated elsewhere.', code: 'conflict' },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await editor(page, first.id).click();
  await page.keyboard.type('Mine. ');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveAttribute('data-state', 'conflict');
  await expect(status(page)).toContainText('Conflict');

  await page.getByTestId(editingTestIds.saveStatusReload).click();

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  await expect(editor(page, first.id)).toContainText('Server copy.');
  await expect(editor(page, first.id)).not.toContainText('Mine.');
});

test('Summary_WhenEdited_ShouldPatchTheScene', async ({ mount, page }) => {
  const patches: unknown[] = [];
  await page.route('**/api/scenes/*', (route) => {
    if (route.request().method() !== 'PATCH') return route.fallback();
    patches.push(route.request().postDataJSON());
    return route.fulfill({ json: { ...first, summary: 'Changed.' } });
  });
  await stubWrite(page, (put) => ({ status: 200, json: { version: put.version + 1 } }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const summary = page
    .getByTestId(writeTestIds.sceneSummary(first.id))
    .getByTestId(proseTestIds.summaryEditor);
  await summary.click();
  await page.keyboard.press('Control+End');
  // One input event: typed key by key, a pause under load longer than the autosave delay saves half the word.
  await page.keyboard.insertText(' Changed.');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  expect(patches).toEqual([{ summary: 'The **hook**. Changed.', title: null, status: null }]);
});

test('Leave_WhenTheSaveFails_ShouldKeepTheEditAndRetryOnReturn', async ({ mount, page }) => {
  // The test decides when each save is answered: the failing one only once the editor has left (so leaving sends it
  // once), the retry only after the kept edit has been seen (under load the 3 s autosave may already have sent it).
  let failFirst = () => {};
  const firstHeld = new Promise<void>((resolve) => (failFirst = resolve));
  let release = () => {};
  const retryHeld = new Promise<void>((resolve) => (release = resolve));
  let attempts = 0;
  const puts = await stubWrite(page, async (put) => {
    if (++attempts === 1) {
      await firstHeld;
      return { status: 503, json: { error: 'Down.', code: 'unavailable' } };
    }
    await retryHeld;
    return { status: 200, json: { version: put.version + 1 } };
  });
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await editor(page, first.id).click();
  await page.keyboard.type('Kept. ');
  await page.getByTestId(testIds.shell.tabPlan).click();
  await expect(page.getByTestId(writeTestIds.page)).toHaveCount(0);
  await expect.poll(() => puts.length).toBe(1);
  const failed = page.waitForResponse((r) => r.url().endsWith('/prose') && r.status() === 503);
  failFirst();
  await failed;

  await page.getByTestId(testIds.shell.tabWrite).click();
  await expect(editor(page, first.id)).toContainText('Kept.');
  await expect(status(page)).toHaveAttribute('data-state', /^(unsaved|saving)$/);
  release();
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  expect(puts).toHaveLength(2);
  expect(puts[1].body).toContain('Kept.');
  expect(puts[1].version).toBe(puts[0].version);
});
