import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import prompts from '../../../../Codex.TestSupport/Fixtures/prompts.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { aiModelChoices } from '../ai/aiFixtures';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { testIds } from '../../testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const beatId = '0199a000-0000-7000-8000-0000000000f1';

type Mode = 'ok' | 'down' | 'offline';

// Stubs the screen; every prose PUT is answered according to `mode.current`.
async function stubWrite(page: Page, beatBody: string | null = null) {
  const mode: { current: Mode } = { current: 'ok' };
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({
      json: {
        bookId: book.id,
        beats:
          beatBody === null
            ? []
            : [
                {
                  id: beatId,
                  sceneId: first.id,
                  body: beatBody,
                  targetWordCount: 400,
                  isHidden: false,
                  contextEntryIds: [],
                },
              ],
      },
    }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({ json: { categories: [], entries: [] } }),
  );
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({ json: { bookId: book.id, drafts: [] } }),
  );
  await page.route('**/api/prompts', (route) => route.fulfill({ json: prompts }));
  // Generate is offered once an AI provider is connected (#36).
  await page.route('**/api/ai/models', (route) => route.fulfill({ json: aiModelChoices }));
  await page.route('**/api/scenes/*/prose', (route) => {
    const request = route.request();
    if (request.method() !== 'PUT') return route.fallback();
    if (mode.current === 'offline') return route.abort('internetdisconnected');
    if (mode.current === 'down') {
      return route.fulfill({ status: 503, json: { error: 'Down.', code: 'unavailable' } });
    }
    const sent = request.postDataJSON() as { version: number };
    return route.fulfill({ json: { version: sent.version + 1 } });
  });
  return mode;
}

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);
const status = (page: Page) => page.getByTestId(editingTestIds.saveStatus);

test('Save_WhenOffline_ShouldKeepTheEditAndSaveByItselfWhenBack', async ({ mount, page }) => {
  const mode = await stubWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  mode.current = 'offline';
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false });
  });

  await editor(page).click();
  await page.keyboard.type('Typed offline. ');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveText('Offline · 1 change waiting');
  await expect(status(page)).toHaveAttribute('data-state', 'offline');
  await expect(editor(page)).toContainText('Typed offline.');
  expect(
    await page.evaluate((id) => window.localStorage.getItem(`codex.unsaved.${id}`), first.id),
  ).toContain('Typed offline.');

  mode.current = 'ok';
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => true });
    window.dispatchEvent(new Event('online'));
  });

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  expect(
    await page.evaluate((id) => window.localStorage.getItem(`codex.unsaved.${id}`), first.id),
  ).toBeNull();
});

test('Save_WhenTheServerFails_ShouldOfferRetryThatSavesTheKeptEdit', async ({ mount, page }) => {
  const mode = await stubWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  mode.current = 'down';

  await editor(page).click();
  await page.keyboard.type('Kept. ');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(status(page)).toHaveText('Unsaved · Retry');
  mode.current = 'ok';
  await page.getByTestId(editingTestIds.saveStatusRetry).click();

  await expect(status(page)).toHaveAttribute('data-state', 'saved');
  await expect(status(page)).toHaveText('Saved');
});

test('Leave_WhenAnEditIsStuck_ShouldAskFirstAndStayWhenDeclined', async ({ mount, page }) => {
  const mode = await stubWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  mode.current = 'down';
  await editor(page).click();
  await page.keyboard.type('Kept. ');
  await page.getByTestId(writeTestIds.manuscript).focus();
  await expect(status(page)).toHaveText('Unsaved · Retry');
  const asked: string[] = [];
  page.on('dialog', (dialog) => {
    asked.push(dialog.message());
    void dialog.dismiss();
  });

  await page.getByTestId(testIds.shell.tabPlan).click();

  await expect.poll(() => asked.length).toBe(1);
  expect(asked[0]).toContain('You have changes that are not saved');
  await expect(page.getByTestId(writeTestIds.page)).toBeVisible();
  await expect(editor(page)).toContainText('Kept.');
});

test('Generate_WhenRateLimited_ShouldCountDownAndKeepRetryDisabledUntilThen', async ({
  mount,
  page,
}) => {
  await page.clock.install();
  await stubWrite(page, 'Guyon crosses.');
  await page.route('**/api/beats/*/generate', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/x-ndjson',
      body: `${JSON.stringify({ error: { error: 'Anthropic is rate limiting requests. Try the draft again in a moment.', code: 'internal_error' } })}\n`,
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatGenerate(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draftError(beatId))).toHaveText(
    'Rate limited. Try again in 30 s',
  );
  await expect(page.getByTestId(writeTestIds.draftRetry(beatId))).toBeDisabled();
  await page.clock.runFor(31_000);
  await expect(page.getByTestId(writeTestIds.draftRetry(beatId))).toBeEnabled();
});

test('Generate_WhenTheBeatIsEmpty_ShouldBeDisabled', async ({ mount, page }) => {
  await stubWrite(page, '');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.beatGenerate(beatId))).toBeDisabled();
});
