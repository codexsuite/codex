import { expect, test } from '@playwright/experimental-ct-react';
import type { Page, Route } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import prompts from '../../../../Codex.TestSupport/Fixtures/prompts.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { aiModelChoices, anthropicIntegrationId, gatewayIntegrationId } from '../ai/aiFixtures';
import { editingTestIds } from '../../editing/testIds';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const [firstScene] = manuscript.scenes;
const beatId = '0199a000-0000-7000-8000-0000000000f1';
const generationId = '0199a000-0000-7000-8000-0000000000a1';

const beat = {
  id: beatId,
  sceneId: first.id,
  body: 'Guyon crosses.',
  targetWordCount: 400,
  isHidden: false,
  contextEntryIds: [] as string[],
};

const draft = {
  id: generationId,
  sceneId: first.id,
  beatId,
  kind: 'beat_prose',
  model: 'claude-opus-5-5',
  templateId: null,
  template: 'General purpose (Nonfiction)',
  targetWords: 400,
  attempt: 2,
  status: 'pending',
  text: 'First paragraph of the draft.\n\nSecond paragraph.',
  words: 7,
  retryOfId: null,
  inputTokens: 10,
  outputTokens: 7,
  integrationId: null,
};

interface Sent {
  method: string;
  url: string;
  body: unknown;
}

// Stubs the screen's reads and the draft list, and answers each draft call with `reply`. Returns what was sent.
async function stubDrafts(
  page: Page,
  drafts: unknown[],
  reply: (sent: Sent) => { status: number; json?: unknown; body?: string; contentType?: string },
) {
  const sent: Sent[] = [];
  const answer = (route: Route) => {
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
    route.fulfill({ json: { categories: [], entries: [] } }),
  );
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({ json: { bookId: book.id, drafts } }),
  );
  await page.route('**/api/prompts', (route) => route.fulfill({ json: prompts }));
  await page.route('**/api/ai/models', (route) => route.fulfill({ json: aiModelChoices }));
  await page.route('**/api/beats/*/generate', answer);
  await page.route('**/api/scenes/*/continue', answer);
  await page.route('**/api/generations/*/*', answer);
  await page.route('**/api/scenes/*/labels', answer);
  return sent;
}

const ndjson = (...events: object[]) => ({
  status: 200,
  contentType: 'application/x-ndjson',
  body: events.map((e) => JSON.stringify(e)).join('\n') + '\n',
});

const started = {
  started: {
    generationId,
    attempt: 1,
    model: 'claude-sonnet-5-5',
    templateId: null,
    template: null,
    targetWords: 400,
  },
};

test('Draft_WhenPending_ShouldShowThePanelWithItsActions', async ({ mount, page }) => {
  await stubDrafts(page, [draft], () => ({ status: 200, json: {} }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const panel = page.getByTestId(writeTestIds.draft(beatId));
  await expect(panel).toHaveAttribute('data-state', 'pending');
  await expect(page.getByTestId(writeTestIds.draftText(beatId))).toContainText('Second paragraph.');
  await expect(page.getByTestId(writeTestIds.draftMeta(beatId))).toContainText('attempt 2');
  await expect(page.getByTestId(writeTestIds.draftApply(beatId))).toBeVisible();
  await expect(page.getByTestId(writeTestIds.draftRetry(beatId))).toBeVisible();
  await expect(page.getByTestId(writeTestIds.draftDiscard(beatId))).toBeVisible();
});

test('Draft_WhenTheBeatHasProse_ShouldNoteHowManyWordsApplyReplaces', async ({ mount, page }) => {
  await stubDrafts(page, [draft], () => ({ status: 200, json: {} }));
  const body = `${firstScene.body}\nOne two three.\n\nFour five.\n`;
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: { ...manuscript, scenes: [{ ...firstScene, body }] } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.draftNote(beatId))).toHaveText(
    'Apply replaces the 5 words below. The current text is kept in history.',
  );
});

test('Draft_WhenTheBeatHasNoProse_ShouldShowNoReplaceNote', async ({ mount, page }) => {
  await stubDrafts(page, [draft], () => ({ status: 200, json: {} }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toBeVisible();
  await expect(page.getByTestId(writeTestIds.draftNote(beatId))).toHaveCount(0);
});

test('Generate_WhenChosen_ShouldPostTheTemplateAndModelAndShowTheDraft', async ({
  mount,
  page,
}) => {
  const sent = await stubDrafts(page, [], () =>
    ndjson(
      started,
      { text: 'Hello ' },
      { text: 'world.' },
      {
        final: {
          generationId,
          model: 'claude-opus-5-5',
          inputTokens: 5,
          outputTokens: 2,
          integrationId: null,
          words: 2,
        },
      },
    ),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatChoice(beatId)).click();
  await page.getByTestId(writeTestIds.draftTemplate('paraphrase')).click();
  await page
    .getByTestId(writeTestIds.draftModel(anthropicIntegrationId, 'claude-opus-5-5'))
    .click();
  await page.getByTestId(writeTestIds.beatGenerate(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveAttribute(
    'data-state',
    'pending',
  );
  await expect(page.getByTestId(writeTestIds.draftText(beatId))).toHaveText('Hello world.');
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    [
      'POST',
      `/api/beats/${beatId}/generate`,
      {
        templateId: 'paraphrase',
        model: 'claude-opus-5-5',
        integrationId: anthropicIntegrationId,
        targetWords: 400,
        retryOfId: null,
      },
    ],
  ]);
});

test('Generate_WhenAModelIsTypedForAnIntegrationWithNone_ShouldPostThatIntegrationAndModel', async ({
  mount,
  page,
}) => {
  const sent = await stubDrafts(page, [], () => ndjson(started));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatChoice(beatId)).click();
  await page.getByTestId(writeTestIds.draftModelText(gatewayIntegrationId)).fill('gpt-5');
  await page.getByTestId(writeTestIds.draftModelUse(gatewayIntegrationId)).click();
  await page.getByTestId(writeTestIds.beatGenerate(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveCount(0);
  expect(sent.map((s) => s.body)).toEqual([
    {
      templateId: 'continue-writing',
      model: 'gpt-5',
      integrationId: gatewayIntegrationId,
      targetWords: 400,
      retryOfId: null,
    },
  ]);
});

test('Draft_WhenNoProviderIsConnected_ShouldLinkToSettingsAndOfferNoGenerate', async ({
  mount,
  page,
}) => {
  await stubDrafts(page, [], () => ({ status: 200, json: {} }));
  await page.route('**/api/ai/models', (route) => route.fulfill({ json: { integrations: [] } }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const connect = page.getByTestId(writeTestIds.draftConnect);
  await expect(connect).toHaveText('Connect an AI provider in Settings');
  await expect(connect).toHaveAttribute('href', '/settings/ai');
  await expect(page.getByTestId(writeTestIds.beatGenerate(beatId))).toHaveCount(0);
});

test('Generate_WhenNotConfigured_ShouldShowTheMessageWithRetry', async ({ mount, page }) => {
  await stubDrafts(page, [], () => ({
    status: 503,
    json: { error: 'AI drafting is not configured.', code: 'not_configured' },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatGenerate(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveAttribute(
    'data-state',
    'failed',
  );
  await expect(page.getByTestId(writeTestIds.draftError(beatId))).toHaveText(
    'AI drafting is not configured.',
  );
  await expect(page.getByTestId(writeTestIds.draftRetry(beatId))).toBeVisible();
});

test('Generate_WhenTheStreamEndsWithAnError_ShouldShowTheFailure', async ({ mount, page }) => {
  await stubDrafts(page, [], () =>
    ndjson(
      started,
      { text: 'Half' },
      { error: { error: 'The provider fell over.', code: 'internal_error' } },
    ),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.beatGenerate(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draftError(beatId))).toHaveText(
    'The provider fell over.',
  );
});

test('Apply_ShouldPostTheSceneVersionAndOfferUndo', async ({ mount, page }) => {
  const appliedBody = `${firstScene.body}\nApplied text.\n`;
  const sent = await stubDrafts(page, [draft], (call) =>
    call.url.endsWith('/apply')
      ? { status: 200, json: { version: firstScene.version + 1, body: appliedBody } }
      : { status: 200, json: { version: firstScene.version + 2, body: firstScene.body } },
  );
  // Applying rewrites the scene, which remounts its beats and refetches the drafts: answer as the server would.
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({
      json: { bookId: book.id, drafts: sent.at(-1)?.url.endsWith('/apply') ? [] : [draft] },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.draftApply(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveCount(0);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Applied 7 words');
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveAttribute(
    'data-state',
    'pending',
  );
  expect(sent.map((s) => [s.url, s.body])).toEqual([
    [`/api/generations/${generationId}/apply`, { version: firstScene.version }],
    [`/api/generations/${generationId}/undo-apply`, { version: firstScene.version + 1 }],
  ]);
});

test('Apply_WhenTheSceneIsStale_ShouldKeepTheDraftAndShowTheConflict', async ({ mount, page }) => {
  await stubDrafts(page, [draft], () => ({
    status: 409,
    json: { error: 'Updated elsewhere.', code: 'conflict' },
  }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.draftApply(beatId)).click();

  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute(
    'data-state',
    'conflict',
  );
  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveAttribute(
    'data-state',
    'pending',
  );
});

test('Discard_ThenUndo_ShouldRestoreTheDraft', async ({ mount, page }) => {
  const sent = await stubDrafts(page, [draft], (call) =>
    call.url.endsWith('/discard')
      ? { status: 200, json: { ...draft, status: 'discarded' } }
      : { status: 200, json: draft },
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.draftDiscard(beatId)).click();
  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveCount(0);
  await page.getByTestId(editingTestIds.toastUndo).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveAttribute(
    'data-state',
    'pending',
  );
  expect(sent.map((s) => s.url)).toEqual([
    `/api/generations/${generationId}/discard`,
    `/api/generations/${generationId}/undo-discard`,
  ]);
});

test('Retry_WhenTheDraftHasAnIntegration_ShouldPostTheSamePickWithRetryOfId', async ({
  mount,
  page,
}) => {
  const integrationId = '3f6c2f0e-5b8a-4a43-9d57-0a1f6c5e7b11';
  const sent = await stubDrafts(page, [{ ...draft, integrationId }], () => ndjson(started));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.draftRetry(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveCount(0);
  expect(sent.map((s) => s.body)).toEqual([
    {
      templateId: null,
      model: 'claude-opus-5-5',
      integrationId,
      targetWords: 400,
      retryOfId: generationId,
    },
  ]);
});

test('Retry_WhenTheDraftsIntegrationWasDeleted_ShouldSendNoModelSoTheDefaultIntegrationChoosesItsOwn', async ({
  mount,
  page,
}) => {
  const sent = await stubDrafts(page, [draft], () => ndjson(started));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.draftRetry(beatId)).click();

  await expect(page.getByTestId(writeTestIds.draft(beatId))).toHaveCount(0);
  expect(sent.map((s) => s.body)).toEqual([
    {
      templateId: null,
      model: '',
      integrationId: null,
      targetWords: 400,
      retryOfId: generationId,
    },
  ]);
});

test('Continue_WhenChosenFromTheMenu_ShouldGenerateFromTheEndOfTheScene', async ({
  mount,
  page,
}) => {
  const sent = await stubDrafts(page, [], () =>
    ndjson(
      started,
      { text: 'More.' },
      {
        final: {
          generationId,
          model: 'claude-sonnet-5-5',
          inputTokens: 1,
          outputTokens: 1,
          integrationId: null,
          words: 1,
        },
      },
    ),
  );
  await page.route('**/api/scenes/*/prose', (route) =>
    route.fulfill({ json: { version: firstScene.version + 1 } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page
    .getByTestId(writeTestIds.scene(first.id))
    .getByTestId(proseTestIds.editor)
    .locator('p')
    .first()
    .click();
  await page.keyboard.press('Control+Home');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('continue-writing')).click();

  await expect(page.getByTestId(writeTestIds.draftText(`scene:${first.id}`))).toHaveText('More.');
  expect(sent.map((s) => [s.url, s.body])).toEqual([
    [
      `/api/scenes/${first.id}/continue`,
      {
        templateId: 'continue-writing',
        model: 'claude-sonnet-5-5',
        integrationId: anthropicIntegrationId,
        targetWords: null,
        retryOfId: null,
      },
    ],
  ]);
});

test('Label_WhenToggled_ShouldPutTheSceneLabels', async ({ mount, page }) => {
  const labelId = '0199a000-0000-7000-8000-0000000000b2';
  const sent = await stubDrafts(page, [], () => ({
    status: 200,
    json: [{ id: labelId, name: 'Drafting', color: 'info' }],
  }));
  await page.route(`**/api/books/${book.id}/labels`, (route) =>
    route.fulfill({
      json: [{ id: labelId, scopeId: book.id, name: 'Drafting', color: 'info', sceneCount: 0 }],
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.sceneLabelAdd(first.id)).click();
  await page.getByTestId(editingTestIds.labelOption(labelId)).click();

  await expect(page.getByTestId(writeTestIds.sceneLabel(first.id))).toHaveText('Drafting');
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['PUT', `/api/scenes/${first.id}/labels`, { labelIds: [first.labels[0].id, labelId] }],
  ]);
});

test('Label_WhenEscapedFromTheKeyboard_ShouldCloseThePickerAndReturnFocusToLabel', async ({
  mount,
  page,
}) => {
  const labelId = '0199a000-0000-7000-8000-0000000000b2';
  await stubDrafts(page, [], () => ({ status: 200, json: [] }));
  await page.route(`**/api/books/${book.id}/labels`, (route) =>
    route.fulfill({
      json: [{ id: labelId, scopeId: book.id, name: 'Drafting', color: 'info', sceneCount: 0 }],
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  const add = page.getByTestId(writeTestIds.sceneLabelAdd(first.id));
  await add.focus();
  await page.keyboard.press('Enter');
  await page.getByTestId(editingTestIds.labelOption(labelId)).focus();

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(editingTestIds.labelOption(labelId))).toHaveCount(0);
  await expect(add).toBeFocused();
});

test('Save_WhenADraftPanelIsOpen_ShouldNotPutTheDraftTextIntoTheProse', async ({ mount, page }) => {
  await stubDrafts(page, [draft], () => ({ status: 200, json: {} }));
  const puts: { body: string; version: number }[] = [];
  await page.route('**/api/scenes/*/prose', (route) => {
    const put = route.request().postDataJSON() as { body: string; version: number };
    puts.push(put);
    return route.fulfill({ json: { version: put.version + 1 } });
  });
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await expect(page.getByTestId(writeTestIds.draftText(beatId))).toContainText('Second paragraph.');

  await page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' More.');
  await page.getByTestId(writeTestIds.manuscript).focus();
  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveAttribute('data-state', 'saved');

  expect(puts).toHaveLength(1);
  expect(puts[0].body).toContain('More.');
  expect(puts[0].body).not.toContain('First paragraph of the draft.');
  expect(puts[0].body).not.toContain('Apply');
});
