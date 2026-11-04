import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);

const found = {
  matches: 3,
  replaceable: 1,
  cited: 1,
  spansLink: 1,
  inDrafts: 2,
  scenes: 2,
  truncated: false,
  items: [
    {
      sceneId: first.id,
      source: 'prose',
      ordinal: 0,
      status: 'replaceable',
      before: 'gave ',
      text: 'teams',
      after: ' a set',
    },
    {
      sceneId: first.id,
      source: 'prose',
      ordinal: 1,
      status: 'cited',
      before: 'the ',
      text: 'teams',
      after: ' copy',
    },
    {
      sceneId: first.id,
      source: 'prose',
      ordinal: 2,
      status: 'spans_link',
      before: 'Kent ',
      text: 'teams',
      after: ' x',
    },
  ],
};

interface Sent {
  path: string;
  body: Record<string, unknown>;
}

async function stub(page: Page) {
  const sent: Sent[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/find`, (route) => {
    sent.push({ path: 'find', body: route.request().postDataJSON() as Record<string, unknown> });
    return route.fulfill({ json: found });
  });
  await page.route(`**/api/books/${book.id}/replace`, (route) => {
    sent.push({ path: 'replace', body: route.request().postDataJSON() as Record<string, unknown> });
    return route.fulfill({
      json: {
        replaced: 1,
        scenes: 1,
        skippedCited: 1,
        skippedSpansLink: 0,
        inDrafts: 0,
        undo: { prose: [], summaries: [], beats: [] },
      },
    });
  });
  await page.route(`**/api/books/${book.id}/replace/undo`, (route) => {
    sent.push({ path: 'undo', body: route.request().postDataJSON() as Record<string, unknown> });
    return route.fulfill({ json: { restored: 1 } });
  });
  return sent;
}

const open = async (page: Page, text: string) => {
  await page.getByTestId(writeTestIds.find).fill(text);
  await page.getByTestId(writeTestIds.replaceToggle).click();
};

test('Replace_WhenOpened_ShouldCountFromTheServerAndListLockedMatchesWithAReason', async ({
  mount,
  page,
}) => {
  await stub(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await open(page, 'teams');

  await expect(page.getByTestId(writeTestIds.replaceCount)).toContainText('3 matches in 2 scenes');
  await expect(page.getByTestId(writeTestIds.replaceCount)).toContainText(
    '1 can be replaced, 2 locked',
  );
  await expect(page.getByTestId(writeTestIds.anyReplaceItem)).toHaveCount(3);
  await expect(page.getByTestId(writeTestIds.replaceItem(1))).toContainText(
    'Locked: inside a cited passage',
  );
  await expect(page.getByTestId(writeTestIds.replaceItem(2))).toContainText('Locked: spans a link');
  await expect(page.getByTestId(writeTestIds.replaceDrafts)).toContainText('2 in a pending draft');
});

test('Replace_WhenTheReplacementIsEmptyOrSameAsTheSearch_ShouldDisableReplace', async ({
  mount,
  page,
}) => {
  await stub(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await open(page, 'teams');
  await expect(page.getByTestId(writeTestIds.replaceCount)).toContainText('3 matches');

  await page.getByTestId(writeTestIds.replaceInput).fill('teams');
  await expect(page.getByTestId(writeTestIds.replaceAll)).toBeDisabled();
  await expect(page.getByTestId(writeTestIds.replaceOne)).toBeDisabled();
  await page.getByTestId(writeTestIds.replaceInput).fill('groups');
  await expect(page.getByTestId(writeTestIds.replaceAll)).toBeEnabled();
  await page.getByTestId(writeTestIds.replaceNext).click();
  await expect(page.getByTestId(writeTestIds.replaceOne)).toBeDisabled();
});

test('ReplaceAll_ShouldPostTheOptionsAndScopeThenToastWithSkippedAndUndo', async ({
  mount,
  page,
}) => {
  const sent = await stub(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await open(page, 'teams');
  await page.getByTestId(writeTestIds.replaceSummaries).check();
  await page.getByTestId(writeTestIds.replaceWholeWord).check();
  await page.getByTestId(writeTestIds.replaceInput).fill('groups');
  await expect(page.getByTestId(writeTestIds.replaceAll)).toBeEnabled();

  await page.getByTestId(writeTestIds.replaceAll).click();

  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Replaced 1 in 1 scene · Skipped 1 inside cited quotes',
  );
  const replace = sent.find((s) => s.path === 'replace')!;
  expect(replace.body).toMatchObject({
    query: 'teams',
    replacement: 'groups',
    scopeKind: 'book',
    prose: true,
    summaries: true,
    beats: false,
    wholeWord: true,
    matchCase: false,
    includeArchived: false,
    only: null,
  });
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect.poll(() => sent.some((s) => s.path === 'undo')).toBe(true);
});

test('Replace_WhenOnTheReplaceableMatch_ShouldSendJustThatMatch', async ({ mount, page }) => {
  const sent = await stub(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await open(page, 'teams');
  await page.getByTestId(writeTestIds.replaceInput).fill('groups');
  await expect(page.getByTestId(writeTestIds.replaceOne)).toBeEnabled();

  await page.getByTestId(writeTestIds.replaceOne).click();

  await expect.poll(() => sent.some((s) => s.path === 'replace')).toBe(true);
  expect(sent.find((s) => s.path === 'replace')!.body.only).toEqual({
    sceneId: first.id,
    source: 'prose',
    ordinal: 0,
  });
});
