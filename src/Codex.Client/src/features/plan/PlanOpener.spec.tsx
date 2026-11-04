import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import labels from '../../../../Codex.TestSupport/Fixtures/structure/labels.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import type { Outline, OutlineOpener } from '@codex/core/api/structure';
import { editingTestIds } from '../../editing/testIds';
import { imagesTestIds } from '../../images/testIds';
import { READY_ASSET, stubPicker } from '../../test-support/imageStubs';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [, first] = act.chapters;

const art: OutlineOpener = {
  assetId: READY_ASSET,
  caption: 'The pass at first light',
  webUrl: `/api/assets/${READY_ASSET}/renditions/web`,
};

const withOpeners = (actOpener: OutlineOpener | null, chapterOpener: OutlineOpener | null) =>
  ({
    ...outline,
    acts: [
      {
        ...act,
        opener: actOpener,
        chapters: act.chapters.map((c) =>
          c.id === first.id ? { ...c, opener: chapterOpener } : c,
        ),
      },
    ],
  }) as Outline;

interface Put {
  url: string;
  body: unknown;
}

// Plan's reads, the picker's, and the opener PUTs: each one is recorded and changes what the outline answers next.
async function stubOpeners(
  page: Page,
  current: { outline: Outline },
  reply: (put: Put) => { status: number; json: unknown },
) {
  const puts: Put[] = [];
  await stubShellApi(page, library);
  await stubPicker(page, { bookId });
  await page.route(`**/api/books/${bookId}/outline`, (route) =>
    route.fulfill({ json: current.outline }),
  );
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route(`**/api/books/${bookId}/labels`, (route) => route.fulfill({ json: labels }));
  await page.route('**/api/*/*/opener', (route) => {
    const put = {
      url: new URL(route.request().url()).pathname,
      body: route.request().postDataJSON(),
    };
    puts.push(put);
    const answer = reply(put);
    if (answer.status < 300) {
      const body = put.body as { assetId: string | null; caption?: string | null };
      const opener =
        body.assetId === null
          ? null
          : { ...art, assetId: body.assetId, caption: body.caption ?? null };
      current.outline = put.url.startsWith('/api/acts/')
        ? withOpeners(opener, current.outline.acts[0]!.chapters[1]!.opener)
        : withOpeners(current.outline.acts[0]!.opener, opener);
    }
    return route.fulfill(answer);
  });
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
  return puts;
}

const open = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

test('ActOpener_WhenSetFromTheActMenu_ShouldPutTheImageAndCaptionAndShowTheThumbnail', async ({
  mount,
  page,
}) => {
  const current = { outline: outline as Outline };
  const puts = await stubOpeners(page, current, () => ({ status: 200, json: {} }));
  await open(mount);

  await page.getByTestId(planTestIds.actMore(act.id)).click();
  await page.getByTestId(planTestIds.actOpener(act.id)).click();
  await expect(page.getByTestId(planTestIds.openerDialog)).toBeVisible();
  await expect(page.getByTestId(planTestIds.openerSave)).toBeDisabled();
  await expect(page.getByTestId(planTestIds.openerClear)).toHaveCount(0);
  await page.getByTestId(imagesTestIds.option(READY_ASSET)).click();
  await page.getByTestId(planTestIds.openerCaption).fill(' The pass at first light ');
  await page.getByTestId(planTestIds.openerSave).click();

  await expect(page.getByTestId(planTestIds.openerDialog)).toHaveCount(0);
  expect(puts).toEqual([
    {
      url: `/api/acts/${act.id}/opener`,
      body: { assetId: READY_ASSET, caption: 'The pass at first light' },
    },
  ]);
  await expect(page.getByTestId(planTestIds.actOpenerThumb(act.id))).toBeVisible();
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Opener art set for Act 1');
});

test('ActOpener_WhenOpened_ShouldJudgeDpiAsAFullPage', async ({ mount, page }) => {
  const current = { outline: outline as Outline };
  await stubOpeners(page, current, () => ({ status: 200, json: {} }));
  const list = page.waitForRequest((r) => r.url().includes(`/api/books/${bookId}/assets?`));
  await open(mount);

  await page.getByTestId(planTestIds.actMore(act.id)).click();
  await page.getByTestId(planTestIds.actOpener(act.id)).click();

  expect(new URL((await list).url()).searchParams.get('placement')).toBe('page');
});

test('ChapterOpener_WhenCleared_ShouldPutNullAndUndoShouldPutItBack', async ({ mount, page }) => {
  const current = { outline: withOpeners(null, art) };
  const puts = await stubOpeners(page, current, () => ({ status: 200, json: {} }));
  await open(mount);

  await page.getByTestId(planTestIds.chapterOpenerThumb(first.id)).click();
  await expect(page.getByTestId(planTestIds.openerCaption)).toHaveValue(art.caption ?? '');
  await page.getByTestId(planTestIds.openerClear).click();

  await expect(page.getByTestId(planTestIds.chapterOpenerThumb(first.id))).toHaveCount(0);
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect(page.getByTestId(planTestIds.chapterOpenerThumb(first.id))).toBeVisible();
  expect(puts.map((p) => p.body)).toEqual([
    { assetId: null },
    { assetId: READY_ASSET, caption: art.caption },
  ]);
});

test('ChapterOpener_WhenTheServerRefuses_ShouldKeepTheDialogOpenWithWhy', async ({
  mount,
  page,
}) => {
  const current = { outline: outline as Outline };
  await stubOpeners(page, current, () => ({
    status: 400,
    json: { error: 'That image is outside the book and its series.', code: 'validation' },
  }));
  await open(mount);

  await page.getByTestId(planTestIds.chapterMore(first.id)).click();
  await page.getByTestId(planTestIds.chapterOpener(first.id)).click();
  await page.getByTestId(imagesTestIds.option(READY_ASSET)).click();
  await page.getByTestId(planTestIds.openerSave).click();

  await expect(page.getByTestId(planTestIds.openerError)).toHaveText(
    'That image is outside the book and its series.',
  );
  await expect(page.getByTestId(planTestIds.openerDialog)).toBeVisible();
});
