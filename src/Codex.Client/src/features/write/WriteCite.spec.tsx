import { expect, test } from '@playwright/experimental-ct-react';
import type { Page, Route } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import markers from '../../../../Codex.TestSupport/Fixtures/references/markers.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import sources from '../../../../Codex.TestSupport/Fixtures/references/sources.json' with { type: 'json' };
import summary from '../../../../Codex.TestSupport/Fixtures/references/clearance-summary.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { stubQuoteFlagsApi } from '../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { refsTestIds } from '../references/testIds';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const [velocity, lean] = sources;
const useId = '0199a000-0000-7000-8000-0000000000a1';
const plain = 'Guyon crossed the yard. He looked back.\n';
const cited = `Guyon crossed the yard. [He looked back.](ref:${useId})\n`;
const marker = { ...markers[0], useId, sceneId: first.id, sourceId: lean.id, generationId: null };

interface Sent {
  method: string;
  url: string;
  body: unknown;
}

// The Write screen with `body` as the first scene's prose. Citation calls are answered by `reply`; the markers
// list is `markersNow()` each time it is read, so a test can change it after a call. Returns what was sent.
async function stubCite(
  page: Page,
  body: string,
  reply: (sent: Sent) => { status: number; json: unknown },
  markersNow: () => unknown[],
) {
  const sent: Sent[] = [];
  const record = (route: Route) => {
    const request = route.request();
    const call = {
      method: request.method(),
      url: new URL(request.url()).pathname,
      body: request.postDataJSON() as unknown,
    };
    sent.push(call);
    return call;
  };
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({
      json: {
        ...manuscript,
        scenes: manuscript.scenes.map((s) => (s.sceneId === first.id ? { ...s, body } : s)),
      },
    }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [] } }),
  );
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({ json: { bookId: book.id, drafts: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({ json: { categories: [], entries: [] } }),
  );
  await page.route(`**/api/books/${book.id}/sources`, (route) => route.fulfill({ json: sources }));
  await page.route(`**/api/books/${book.id}/markers`, (route) =>
    route.fulfill({ json: markersNow() }),
  );
  await page.route('**/api/scenes/*/prose', (route) => {
    const call = record(route);
    return route.fulfill({ json: { version: (call.body as { version: number }).version + 1 } });
  });
  await page.route('**/api/citations', (route) => route.fulfill(reply(record(route))));
  await page.route('**/api/citations/*/remove', (route) => route.fulfill(reply(record(route))));
  return sent;
}

const open = (mount: (component: React.JSX.Element, options: object) => unknown) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);

// Selects `text` in the scene (its last match when `last`; a caret after it when `collapse`), and has ProseMirror read the selection before
// returning: a selectionchange dispatched at once, so its 20 ms focus timer finds nothing to put back.
const select = (page: Page, text: string, collapse = false, last = false) =>
  editor(page).evaluate(
    (element, [wanted, atEnd, fromEnd]) => {
      (element as HTMLElement).focus();
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const at = fromEnd
          ? node.textContent!.lastIndexOf(wanted as string)
          : node.textContent!.indexOf(wanted as string);
        if (at < 0) continue;
        const end = at + (wanted as string).length;
        window.getSelection()!.setBaseAndExtent(node, atEnd ? end : at, node, end);
        document.dispatchEvent(new Event('selectionchange'));
        return;
      }
      throw new Error(`"${wanted as string}" is not in the scene`);
    },
    [text, collapse, last] as const,
  );

const citeOk = (call: Sent) => ({
  status: 200,
  json: {
    useId,
    sceneId: first.id,
    version: ((call.body as { version: number }).version ?? 0) + 1,
    body: cited,
    number: 1,
    markersShifted: 0,
    statusBefore: 'not_cited',
    status: 'needs_check',
    reason: 'ready_to_sign_off',
  },
});

const citeCalls = (sent: Sent[]) => sent.filter((s) => s.url === '/api/citations');
const saves = (sent: Sent[]) =>
  sent.filter((s) => s.method === 'PUT').map((s) => s.body as { body: string; version: number });

test('Picker_WhenSlashIsTypedOverASelection_ShouldKeepTheWordsAndCiteThem', async ({
  mount,
  page,
}) => {
  let markersNow: unknown[] = [];
  const sent = await stubCite(
    page,
    plain,
    (call) => {
      markersNow = [marker];
      return citeOk(call);
    },
    () => markersNow,
  );
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');
  // An edit first, so the scene always has something to save before it is cited.
  await select(page, 'Guyon', true);
  await page.keyboard.type(' Hale');

  await select(page, 'He looked back.');
  await page.keyboard.type('/source');

  await expect(editor(page)).toContainText('He looked back. /source');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();

  await expect(page.getByTestId(writeTestIds.citeOption(velocity.id))).toHaveAttribute(
    'data-tone',
    'danger',
  );
  await expect(page.getByTestId(writeTestIds.citeOption(lean.id))).toHaveAttribute(
    'data-tone',
    'muted',
  );
  await expect(page.getByTestId(writeTestIds.citeNewSource)).toBeEnabled();
  await page.getByTestId(writeTestIds.citeSearch).fill('lean');
  await expect(page.getByTestId(writeTestIds.citeOption(velocity.id))).toHaveCount(0);
  await page.getByTestId(writeTestIds.citeOption(lean.id)).click();

  await expect(page.getByTestId(writeTestIds.marker(useId))).toHaveText('1');
  await expect(page.getByTestId(writeTestIds.citePicker)).toHaveCount(0);
  // What the menu typed is gone again by the time the passage is read: the last save before citing is the text,
  // and the cite is sent at the version that save made.
  const last = saves(sent).at(-1)!;
  expect(last.body).toBe('Guyon Hale crossed the yard. He looked back.\n');
  expect(citeCalls(sent).map((s) => s.body)).toEqual([
    {
      sceneId: first.id,
      sourceId: lean.id,
      version: last.version + 1,
      passage: 'He looked back.',
      occurrence: 1,
    },
  ]);
});

test('Picker_WhenTheBlurSaveIsStillInFlight_ShouldWaitForItAndCiteAtTheVersionItMade', async ({
  mount,
  page,
}) => {
  const sent = await stubCite(page, plain, citeOk, () => []);
  // The first save (the blur's, with the menu's `/source` still in the text) is answered only when released.
  let release = () => {};
  const held = new Promise<void>((resolve) => (release = resolve));
  let first = true;
  await page.route('**/api/scenes/*/prose', async (route) => {
    if (!first) return route.fallback();
    first = false;
    const body = route.request().postDataJSON() as { body: string; version: number };
    sent.push({ method: 'PUT', url: new URL(route.request().url()).pathname, body });
    await held;
    return route.fulfill({ json: { version: body.version + 1 } });
  });
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'He looked back.');
  await page.keyboard.type('/source');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
  await page.getByTestId(writeTestIds.citeOption(lean.id)).click();
  await expect.poll(() => saves(sent).length).toBeGreaterThan(0);
  expect(saves(sent)[0].body).toContain('/source');
  release();

  await expect.poll(() => citeCalls(sent).length).toBe(1);
  // The cite waited for the held save, then saved the text without `/source` and sent the version that made.
  const last = saves(sent).at(-1)!;
  expect(last.body).toBe(plain);
  expect((citeCalls(sent)[0].body as { version: number }).version).toBe(last.version + 1);
});

test('Picker_WhenNothingIsSelected_ShouldCiteTheSentenceBeforeTheCaret', async ({
  mount,
  page,
}) => {
  const sent = await stubCite(page, plain, citeOk, () => []);
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'Guyon crossed the yard.', true);
  await page.keyboard.type(' /sou');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
  await page.getByTestId(writeTestIds.citeOption(velocity.id)).click();

  await expect.poll(() => citeCalls(sent).length).toBe(1);
  // The space typed before `/` stays the author's; the `/sou` is gone from the save before the cite.
  const last = saves(sent).at(-1)!;
  expect(last.body).not.toContain('/');
  expect(citeCalls(sent)[0].body).toEqual({
    sceneId: first.id,
    sourceId: velocity.id,
    version: last.version + 1,
    passage: 'Guyon crossed the yard.',
    occurrence: 1,
  });
});

test('Picker_WhenTheStoredBodyIsInAnotherForm_ShouldSaveTheEditorsFormBeforeCiting', async ({
  mount,
  page,
}) => {
  // No trailing newline: the editor writes the same words in its own (canonical) form.
  const sent = await stubCite(page, 'He looked back. He looked back.', citeOk, () => []);
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'He looked back.', false, true);
  await page.keyboard.type('/');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
  await page.getByTestId(writeTestIds.citeOption(lean.id)).click();

  await expect.poll(() => citeCalls(sent).length).toBe(1);
  const last = saves(sent).at(-1)!;
  expect(last.body).toBe('He looked back. He looked back.\n');
  expect(citeCalls(sent)[0].body).toMatchObject({
    version: last.version + 1,
    passage: 'He looked back.',
    occurrence: 2,
  });
});

test('Menu_WhenEscapedAfterSlashOverASelection_ShouldLeaveTheTextAndKeepTheSelection', async ({
  mount,
  page,
}) => {
  const sent = await stubCite(page, plain, citeOk, () => []);
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'He looked back.');
  await page.keyboard.type('/sou');
  await expect(page.getByTestId(proseTestIds.slashMenu)).toBeVisible();
  await page.keyboard.press('Escape');

  await expect(page.getByTestId(proseTestIds.slashMenu)).toHaveCount(0);
  await expect(editor(page)).toHaveText('Guyon crossed the yard. He looked back.');
  // The words are still selected: typing replaces them.
  await page.keyboard.type('Z');
  await expect(editor(page)).toHaveText('Guyon crossed the yard. Z');
  expect(citeCalls(sent)).toHaveLength(0);
});

test('Menu_WhenAnotherItemIsPickedAfterSlashOverASelection_ShouldLeaveNoStraySpace', async ({
  mount,
  page,
}) => {
  const sent = await stubCite(page, plain, citeOk, () => []);
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'He looked back.');
  await page.keyboard.type('/block');
  await page.getByTestId(proseTestIds.slashItem('block-quote')).click();

  await expect(editor(page).locator('blockquote')).toHaveText(
    'Guyon crossed the yard. He looked back.',
  );
  // The caret is where the words end: typing goes straight after them.
  await page.keyboard.type('!');
  await expect(editor(page)).toHaveText('Guyon crossed the yard. He looked back.!');
  expect(citeCalls(sent)).toHaveLength(0);
});

test('Picker_WhenNewSourceIsPicked_ShouldSaveAndOpenAddSourceToReturnHere', async ({
  mount,
  page,
}) => {
  const sent = await stubCite(page, plain, citeOk, () => []);
  await stubQuoteFlagsApi(page, book.id, []);
  await page.route(`**/api/books/${book.id}/references/summary`, (route) =>
    route.fulfill({ json: summary }),
  );
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');
  await select(page, 'Guyon', true);
  await page.keyboard.type(' Hale');

  await select(page, 'He looked back.');
  await page.keyboard.type('/source');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
  await page.getByTestId(writeTestIds.citeNewSource).click();

  // Saved before leaving: the selection is a range of what the server holds.
  await expect(page.getByTestId(refsTestIds.addReturnNote)).toHaveText(
    `Will return to Write · Chapter ${first.number.replace('.', ' · Scene ')}`,
  );
  expect(saves(sent).at(-1)?.body).toBe('Guyon Hale crossed the yard. He looked back.\n');
  await expect(page.getByTestId(refsTestIds.addLookup)).toHaveText('Save');
  expect(citeCalls(sent)).toHaveLength(0);
});

test('Cite_WhileTheRequestIsInFlight_ShouldKeepTheSceneReadOnly', async ({ mount, page }) => {
  let markersNow: unknown[] = [];
  let answer: () => void = () => {};
  const answered = new Promise<void>((resolve) => (answer = resolve));
  await stubCite(page, plain, citeOk, () => markersNow);
  await page.route('**/api/citations', async (route) => {
    await answered;
    markersNow = [marker];
    await route.fulfill(citeOk({ method: 'POST', url: '', body: route.request().postDataJSON() }));
  });
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');

  await select(page, 'He looked back.');
  await page.keyboard.type('/');
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
  await page.getByTestId(writeTestIds.citeOption(lean.id)).click();

  await expect(editor(page)).toHaveAttribute('contenteditable', 'false');
  answer();
  await expect(page.getByTestId(writeTestIds.marker(useId))).toHaveText('1');
  await expect(editor(page)).toHaveAttribute('contenteditable', 'true');
});

for (const refusal of [
  { status: 409, code: 'conflict', error: 'Updated elsewhere.' },
  { status: 400, code: 'validation', error: 'The passage is not in the scene.' },
]) {
  test(`Picker_WhenTheServerAnswers${refusal.status}_ShouldToastTheReason`, async ({
    mount,
    page,
  }) => {
    const sent = await stubCite(
      page,
      plain,
      () => ({ status: refusal.status, json: { error: refusal.error, code: refusal.code } }),
      () => [],
    );
    await open(mount);
    await expect(editor(page)).toContainText('He looked back.');

    await select(page, 'He looked back.');
    await page.keyboard.type('/');
    await page.getByTestId(proseTestIds.slashItem('source-reference')).click();
    await page.getByTestId(writeTestIds.citeOption(lean.id)).click();

    await expect(page.getByTestId(editingTestIds.toast)).toContainText(refusal.error);
    expect(citeCalls(sent)).toHaveLength(1);
    await expect(editor(page)).toContainText('Guyon crossed the yard. He looked back.');
    if (refusal.status === 409) {
      await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute(
        'data-state',
        'conflict',
      );
    }
  });
}

test('MarkerCard_WhenRemoveIsClicked_ShouldUnwrapTheMarkerAtTheSavedVersion', async ({
  mount,
  page,
}) => {
  let markersNow: unknown[] = [marker];
  const sent = await stubCite(
    page,
    cited,
    () => {
      markersNow = [];
      return {
        status: 200,
        json: {
          useId,
          sceneId: first.id,
          version: 5,
          body: plain,
          markersShifted: 0,
          status: 'not_cited',
        },
      };
    },
    () => markersNow,
  );
  await open(mount);

  await page.getByTestId(writeTestIds.marker(useId)).click();
  await page.getByTestId(writeTestIds.citeRemove).click();

  await expect(page.getByTestId(writeTestIds.marker(useId))).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(0);
  await expect(editor(page)).toContainText('He looked back.');
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['POST', `/api/citations/${useId}/remove`, { version: 4 }],
  ]);
});

test('MarkerCard_WhenTheMarkerIsAtTheBottomOfTheWindow_ShouldKeepRemoveInTheWindow', async ({
  mount,
  page,
}) => {
  await stubCite(
    page,
    cited,
    () => ({ status: 500, json: {} }),
    () => [marker],
  );
  await open(mount);
  const number = page.getByTestId(writeTestIds.marker(useId));
  await expect(number).toBeVisible();
  const box = await number.boundingBox();
  const width = page.viewportSize()?.width ?? 1280;
  await page.setViewportSize({ width, height: Math.ceil((box?.y ?? 0) + (box?.height ?? 0) + 20) });

  await number.click();

  await expect(page.getByTestId(writeTestIds.citeRemove)).toBeInViewport({ ratio: 1 });
});

test('MarkerCard_WhenRemoveIsRefused_ShouldToastTheReasonAndKeepTheMarker', async ({
  mount,
  page,
}) => {
  await stubCite(
    page,
    cited,
    () => ({ status: 404, json: { error: 'Source marker not found.', code: 'not_found' } }),
    () => [marker],
  );
  await open(mount);

  await page.getByTestId(writeTestIds.marker(useId)).click();
  await page.getByTestId(writeTestIds.citeRemove).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Source marker not found.');
  await expect(page.getByTestId(writeTestIds.marker(useId))).toHaveText('1');
  // The card stays open, so the author can try again or follow it to References.
  await expect(page.getByTestId(writeTestIds.markerCard)).toBeVisible();
});

test('Picker_WhenOpenedFromTheKeyboardAndEscaped_ShouldCloseAndReturnFocusToTheEditor', async ({
  mount,
  page,
}) => {
  await stubCite(page, plain, citeOk, () => []);
  await open(mount);
  await expect(editor(page)).toContainText('He looked back.');
  await select(page, 'He looked back.');
  await page.keyboard.type('/source');
  await expect(page.getByTestId(proseTestIds.slashItem('source-reference'))).toBeVisible();

  await page.keyboard.press('Enter');
  await expect(page.getByTestId(writeTestIds.citeSearch)).toBeFocused();
  await page.keyboard.press('Escape');

  await expect(page.getByTestId(writeTestIds.citePicker)).toHaveCount(0);
  await expect(editor(page)).toBeFocused();
});
