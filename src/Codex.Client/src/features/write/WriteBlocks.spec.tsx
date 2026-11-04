import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first, second] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const beatId = '0199a000-0000-7000-8000-0000000000f1';
const entryId = '0199a000-0000-7000-8000-0000000000c1';

const beat = {
  id: beatId,
  sceneId: first.id,
  body: 'Guyon crosses.',
  targetWordCount: 400,
  isHidden: false,
  contextEntryIds: [] as string[],
  title: '',
};

interface Sent {
  method: string;
  url: string;
  body: unknown;
}

// Stubs the screen's reads, the beat list and the KB, and answers block calls with `reply`. Returns what was sent.
async function stubBlocks(page: Page, reply: (sent: Sent) => { status: number; json?: unknown }) {
  const sent: Sent[] = [];
  const answer = (route: import('playwright').Route) => {
    const request = route.request();
    const call = {
      method: request.method(),
      url: new URL(request.url()).pathname,
      body: request.postDataJSON() as unknown,
    };
    sent.push(call);
    return route.fulfill(reply(call));
  };
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [beat] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({
      json: {
        categories: [{ id: 'c', name: 'Characters', fieldDefs: [], fromSeries: false }],
        entries: [
          { id: entryId, categoryId: 'c', name: 'Guyon', aliases: [], sceneCount: 0, useCount: 0 },
        ],
      },
    }),
  );
  await page.route('**/api/beats/*', answer);
  await page.route('**/api/beats/*/context', answer);
  await page.route('**/api/blocks/move', answer);
  await page.route('**/api/scenes/*/beats', answer);
  await page.route('**/api/scenes/*/split', answer);
  await page.route('**/api/scenes/*/prose', answer);
  await page.route('**/api/scenes/*/kb', answer);
  return sent;
}

const editor = (page: Page, sceneId: string) =>
  page.getByTestId(writeTestIds.scene(sceneId)).getByTestId(proseTestIds.editor);

// Puts the caret at the start of the scene's first paragraph with the click itself. Not click then Control+Home:
// ProseMirror re-applies its own selection 20 ms after the editor gains focus, and under load that can land after
// the keyboard has moved the caret but before ProseMirror has read the move, putting the caret back at the click.
const caretAtStart = (page: Page, sceneId: string) =>
  editor(page, sceneId)
    .locator('p')
    .first()
    .click({ position: { x: 0, y: 1 } });

test('Menu_WhenADisabledItemIsChosen_ShouldNotRun', async ({ mount, page }) => {
  const sent = await stubBlocks(page, () => ({ status: 200, json: {} }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await caretAtStart(page, first.id);
  await page.keyboard.type(' /');
  await expect(page.getByTestId(proseTestIds.slashItem('source-reference'))).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await page.getByTestId(proseTestIds.slashItem('source-reference')).click({ force: true });

  expect(sent).toHaveLength(0);
  await expect(editor(page, first.id)).toContainText('/');
});

test('Length_WhenChosen_ShouldPatchTheBeat', async ({ mount, page }) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatLength(beatId, 600)).click();

  await expect(page.getByTestId(writeTestIds.beatLength(beatId, 600))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['PATCH', `/api/beats/${beatId}`, { targetWordCount: 600 }],
  ]);
});

test('Context_WhenAdded_ShouldPutTheEntryIds', async ({ mount, page }) => {
  const sent = await stubBlocks(page, () => ({ status: 204 }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatContextAdd(beatId)).click();
  await page.getByTestId(writeTestIds.beatContextOption(entryId)).click();

  await expect(page.getByTestId(writeTestIds.beatContextChip(beatId, entryId))).toContainText(
    'Guyon',
  );
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['PUT', `/api/beats/${beatId}/context`, { entryIds: [entryId] }],
  ]);
});

test('Title_WhenTyped_ShouldPatchTheBeatTitleTrimmed', async ({ mount, page }) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const title = page.getByTestId(writeTestIds.beatTitle(beatId));
  await title.fill(' Into the yard ');
  await title.press('Enter');

  await expect
    .poll(() => sent.map((s) => [s.method, s.url, s.body]))
    .toEqual([['PATCH', `/api/beats/${beatId}`, { title: 'Into the yard' }]]);
});

test('Clear_ShouldEmptyTheInstructionAndPatchIt', async ({ mount, page }) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatClear(beatId)).click();

  await expect(page.getByTestId(writeTestIds.beatBody(beatId))).toHaveText('');
  await expect
    .poll(() => sent.map((s) => [s.method, s.url, s.body]))
    .toEqual([['PATCH', `/api/beats/${beatId}`, { body: '' }]]);
});

const instruction = (page: Page) => page.getByTestId(writeTestIds.beatBody(beatId));

test('Instruction_WhenTypedInRichAndBlurred_ShouldPatchTheBodyOnce', async ({ mount, page }) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await instruction(page).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' Then he waits.');
  expect(sent).toEqual([]);
  await page.getByTestId(writeTestIds.beatTitle(beatId)).click();

  await expect
    .poll(() => sent.map((s) => [s.method, s.url, s.body]))
    .toEqual([['PATCH', `/api/beats/${beatId}`, { body: 'Guyon crosses. Then he waits.' }]]);
});

test('Instruction_WhenToggledToSourceAndBack_ShouldSendNothingAndKeepTheText', async ({
  mount,
  page,
}) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await instruction(page).click();
  await page.getByTestId(proseTestIds.modeSource('beat-instruction')).click();
  const source = page.getByTestId(proseTestIds.sourceEditor('beat-instruction'));
  await expect(source).toHaveText('Guyon crosses.');
  await page.getByTestId(proseTestIds.modeRich('beat-instruction')).click();
  await expect(instruction(page)).toHaveText('Guyon crosses.');
  await page.getByTestId(writeTestIds.beatTitle(beatId)).click();

  expect(sent).toEqual([]);
});

test('Instruction_WhenTypedInSourceAndBlurred_ShouldPatchTheBodyAndGenerateShouldSaveFirst', async ({
  mount,
  page,
}) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { ...beat, ...(call.body as object) },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await instruction(page).click();
  await page.getByTestId(proseTestIds.modeSource('beat-instruction')).click();
  const source = page.getByTestId(proseTestIds.sourceEditor('beat-instruction'));
  await source.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' **Hard.**');
  await page.getByTestId(writeTestIds.beatTitle(beatId)).click();

  await expect
    .poll(() => sent.map((s) => [s.method, s.url, s.body]))
    .toEqual([['PATCH', `/api/beats/${beatId}`, { body: 'Guyon crosses. **Hard.**' }]]);
});

test('Instruction_WhenControlSlashPressed_ShouldSwitchOnlyTheBeatNotTheScene', async ({
  mount,
  page,
}) => {
  await stubBlocks(page, () => ({ status: 200, json: beat }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await instruction(page).click();
  await page.keyboard.press('Control+/');

  await expect(page.getByTestId(proseTestIds.sourceEditor('beat-instruction'))).toBeVisible();
  await expect(editor(page, first.id)).toBeVisible();
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toHaveCount(0);
});

test('Delete_WhenChosenFromTheMenu_ShouldSaveTheSceneWithoutTheBeatAndUndoShouldPutItBack', async ({
  mount,
  page,
}) => {
  const [firstBody] = manuscript.scenes;
  let version = firstBody.version;
  const sent = await stubBlocks(page, (call) =>
    call.url.endsWith('/prose') ? { status: 200, json: { version: ++version } } : { status: 200 },
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatMore(beatId)).click();
  await page.getByTestId(writeTestIds.beatDelete(beatId)).click();

  await expect(page.getByTestId(proseTestIds.directiveCard('beat'))).toHaveCount(0);
  const saves = () => sent.filter((s) => s.url.endsWith('/prose'));
  expect(saves()[0]).toEqual({
    method: 'PUT',
    url: `/api/scenes/${first.id}/prose`,
    body: { body: 'Guyon crossed the yard.\n', version: firstBody.version },
  });

  await page.getByTestId(editingTestIds.toastUndo).click();

  await expect(page.getByTestId(proseTestIds.directiveCard('beat'))).toHaveCount(1);
  expect(saves()[1]?.body).toEqual({ body: firstBody.body, version: firstBody.version + 1 });
});

test('Codex_WhenAnEntryIsPicked_ShouldLinkItToTheScene', async ({ mount, page }) => {
  const sent = await stubBlocks(page, () => ({ status: 204 }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.sceneEntryAdd(second.id)).click();
  await page.getByTestId(writeTestIds.entryPickerOption(entryId)).click();

  await expect(page.getByTestId(writeTestIds.entryPicker)).toHaveCount(0);
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['POST', `/api/scenes/${second.id}/kb`, { entryId }],
  ]);
});

test('Move_WhenTheServerSaysStale_ShouldKeepTheTextAndShowTheConflict', async ({ mount, page }) => {
  const sent = await stubBlocks(page, () => ({
    status: 409,
    json: { error: 'Updated elsewhere.', code: 'conflict' },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatGrip(beatId)).focus();
  await page.keyboard.press('Alt+ArrowDown');

  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute(
    'data-state',
    'conflict',
  );
  expect(sent).toHaveLength(1);
  expect(sent[0].url).toBe('/api/blocks/move');
  const move = sent[0].body as {
    fromBody: string;
    toBody: string;
    fromSceneId: string;
    toSceneId: string;
  };
  expect(move.fromSceneId).toBe(first.id);
  expect(move.toSceneId).toBe(second.id);
  expect(move.fromBody).not.toContain(beatId);
  expect(move.toBody).toContain(beatId);
  await expect(page.getByTestId(writeTestIds.beatGrip(beatId))).toBeVisible();
  await expect(editor(page, first.id)).toContainText('Guyon crossed the yard.');
});

test('Menu_WhenSceneBeatIsChosen_ShouldSendTheSplitBodyWithANewBeatLine', async ({
  mount,
  page,
}) => {
  const sent = await stubBlocks(page, (call) => ({
    status: 200,
    json: { beatId: (call.body as { beatId: string }).beatId, version: 5 },
  }));
  await page.route('**/api/scenes/*/prose', (route) => route.fulfill({ json: { version: 5 } }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await caretAtStart(page, first.id);
  await page.keyboard.type('Guyon /scene');
  await page.getByTestId(proseTestIds.slashItem('scene-beat')).click();

  await expect.poll(() => sent.length).toBe(1);
  expect(sent[0].url).toBe(`/api/scenes/${first.id}/beats`);
  const body = (sent[0].body as { body: string; version: number }).body.split('\n\n');
  expect(body[0]).toBe('Guyon');
  expect(body[1]).toMatch(/^::beat\{id="/);
  expect(body[2]).toBe('Guyon crossed the yard.');
});

test('Drag_WhenTheGripIsDroppedOnAnotherScene_ShouldMoveTheBeatWithItsProse', async ({
  mount,
  page,
}) => {
  const sent = await stubBlocks(page, () => ({
    status: 200,
    json: { fromVersion: 5, toVersion: 5 },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatGrip(beatId)).dragTo(editor(page, second.id));

  await expect.poll(() => sent.length).toBe(1);
  const move = sent[0].body as { fromSceneId: string; toSceneId: string; toBody: string };
  expect(move.fromSceneId).toBe(first.id);
  expect(move.toSceneId).toBe(second.id);
  expect(move.toBody).toContain(beatId);
});
