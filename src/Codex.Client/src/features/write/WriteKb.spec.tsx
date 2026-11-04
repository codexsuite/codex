import { expect, test } from '@playwright/experimental-ct-react';
import type { Page, Route } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import entryFixture from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { proseTestIds } from '../../prose/testIds';
import { HashProbe } from '../../test-support/HashProbe';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const entryId = '0199a000-0000-7000-8000-0000000000c1';
const hashProbeId = 'test-hash-probe';

interface Sent {
  method: string;
  url: string;
  body: unknown;
}

const withBody = (body: string) => ({
  ...manuscript,
  scenes: manuscript.scenes.map((s) => (s.sceneId === first.id ? { ...s, body } : s)),
});

// The screen's reads with `body` as the first scene's prose, a one-entry Knowledge base ("Guyon", alias "the guide")
// and the additions inserted so far. Returns what was sent to the prose and addition endpoints.
async function stubKb(page: Page, body: string) {
  const sent: Sent[] = [];
  const additions: unknown[] = [];
  const record = (route: Route) => {
    const request = route.request();
    sent.push({
      method: request.method(),
      url: new URL(request.url()).pathname,
      body: request.postDataJSON() as unknown,
    });
  };
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: withBody(body) }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({
      json: {
        categories: [
          {
            id: 'c',
            name: 'Characters',
            colour: 'category-person',
            fieldDefs: [],
            fromSeries: false,
          },
        ],
        entries: [
          {
            id: entryId,
            categoryId: 'c',
            name: 'Guyon',
            aliases: ['the guide'],
            sceneCount: 1,
            useCount: 2,
            fromSeries: false,
            seriesName: null,
            alwaysInContext: false,
            summaryLine: '',
          },
        ],
      },
    }),
  );
  await page.route('**/api/kb/entries/*', (route) =>
    route.fulfill({
      json: {
        ...entryFixture,
        id: entryId,
        usedIn: [{ ...entryFixture.usedIn[0], sceneId: first.id }],
      },
    }),
  );
  await page.route('**/api/scenes/*/prose', (route) => {
    record(route);
    return route.fulfill({ json: { version: 9 } });
  });
  await page.route('**/api/scenes/*/kb-additions', (route) => {
    record(route);
    const request = route.request().postDataJSON() as { additionId: string; entryId: string };
    additions.push({
      id: request.additionId,
      sceneId: first.id,
      entryId: request.entryId,
      body: '',
    });
    return route.fulfill({ json: { progressionId: request.additionId, version: 5 } });
  });
  return sent;
}

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);

const openWrite = (mount: (component: React.JSX.Element, options: object) => unknown, hash = '') =>
  mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${book.id}/write${hash}` } },
  );

test('Text_WhenEntryDropped_ShouldInsertMention', async ({ mount, page }) => {
  await stubKb(page, 'The yard was empty.\n');
  await openWrite(mount);
  const paragraph = editor(page).locator('p').first();
  const box = (await paragraph.boundingBox())!;
  const transfer = await page.evaluateHandle(
    ([id, name]) => {
      const data = new DataTransfer();
      data.setData('application/x-codex-entry', JSON.stringify({ entryId: id, name }));
      return data;
    },
    [entryId, 'Guyon'],
  );

  await paragraph.dispatchEvent('drop', {
    dataTransfer: transfer,
    clientX: box.x + 4,
    clientY: box.y + box.height / 2,
  });

  const mention = editor(page).getByTestId(writeTestIds.kbMention);
  await expect(mention).toHaveCount(1);
  await expect(mention).toHaveText('Guyon');
  await expect(mention).toHaveAttribute('data-kb-entry', entryId);
});

test('Alias_WhenNameIsPlainText_ShouldBeUnderlinedInTheCategoryColour', async ({ mount, page }) => {
  await stubKb(page, 'Guyon followed, and the Guyonic road.\n');
  await openWrite(mount);

  const marks = editor(page).getByTestId(writeTestIds.kbAlias);
  await expect(marks).toHaveCount(1);
  await expect(marks).toHaveText('Guyon');
  await expect(marks).toHaveCSS('border-bottom-style', 'dotted');
  await expect(marks).toHaveAttribute('style', /--category-person/);
});

test('Mention_WhenCtrlClicked_ShouldOpenTheEntryHash', async ({ mount, page }) => {
  await stubKb(page, `[Guyon](kb:${entryId}) crossed.\n`);
  await openWrite(mount, '#scene=1.1');

  await editor(page).getByTestId(writeTestIds.kbMention).click();
  await expect(page.getByTestId(hashProbeId)).toHaveText('#scene=1.1');
  await editor(page)
    .getByTestId(writeTestIds.kbMention)
    .click({ modifiers: ['Control'] });

  await expect(page.getByTestId(hashProbeId)).toHaveText(`#entry=${entryId}`);
});

test('Highlight_ShouldStepThroughUsesAndClear', async ({ mount, page }) => {
  await stubKb(page, `[Guyon](kb:${entryId}) met the guide at dawn.\n`);
  await openWrite(mount, `#kb=${entryId}`);

  const position = page.getByTestId(writeTestIds.highlightPosition);
  await expect(page.getByTestId(writeTestIds.highlightName)).toHaveText('Guyon');
  const current = () =>
    page.evaluate(() => [...(CSS.highlights.get('kb-current') ?? [])].map((r) => r.toString()));
  await expect(position).toHaveText('1 of 2');
  await page.getByTestId(writeTestIds.highlightNext).click();
  await expect(position).toHaveText('1 of 2');
  await expect.poll(current).toEqual(['Guyon']);
  await page.getByTestId(writeTestIds.highlightNext).click();
  await expect(position).toHaveText('2 of 2');
  await expect.poll(current).toEqual(['the guide']);
  await page.getByTestId(writeTestIds.highlightNext).click();
  await expect(position).toHaveText('1 of 2');
  await expect(page.getByTestId(writeTestIds.minimapTick(first.id))).toHaveAttribute(
    'data-used',
    'true',
  );

  await page.getByTestId(writeTestIds.highlightClear).click();

  await expect(page.getByTestId(writeTestIds.highlightChip)).toHaveCount(0);
  await expect(page.getByTestId(hashProbeId)).toHaveText('');
  await expect(page.getByTestId(writeTestIds.minimapTick(first.id))).toHaveAttribute(
    'data-used',
    'false',
  );
});

test('Addition_WhenInserted_ShouldSendTheBodyWithItsLineAndShowTheCard', async ({
  mount,
  page,
}) => {
  const sent = await stubKb(page, 'The yard was empty.\n');
  await openWrite(mount);

  await editor(page).locator('p').first().click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('codex-addition')).click();
  await page.getByTestId(writeTestIds.entryPickerOption(entryId)).click();

  const posts = () => sent.filter((s) => s.url.endsWith('/kb-additions'));
  await expect.poll(() => posts().length).toBe(1);
  const call = posts()[0]!;
  const request = call.body as {
    additionId: string;
    entryId: string;
    sceneBody: string;
    version: number;
  };
  expect([call.method, call.url, request.entryId, request.version]).toEqual([
    'POST',
    `/api/scenes/${first.id}/kb-additions`,
    entryId,
    9,
  ]);
  expect(request.sceneBody).toContain(`::kb-addition{id="${request.additionId}"}`);
  await expect(page.getByTestId(writeTestIds.addition(request.additionId))).toBeVisible();
});

test('Addition_WhenTypedInEitherViewAndBlurred_ShouldPatchTheBody', async ({ mount, page }) => {
  const sent = await stubKb(page, 'The yard was empty.\n');
  const patches: unknown[] = [];
  await page.route('**/api/kb-additions/*', (route) => {
    const body = route.request().postDataJSON() as { body: string };
    patches.push(body);
    return route.fulfill({
      json: { id: 'x', sceneId: first.id, entryId, progressionId: 'x', body: body.body },
    });
  });
  await openWrite(mount);
  await editor(page).locator('p').first().click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('codex-addition')).click();
  await page.getByTestId(writeTestIds.entryPickerOption(entryId)).click();
  await expect.poll(() => sent.filter((s) => s.url.endsWith('/kb-additions')).length).toBe(1);
  const card = page.locator('[data-testid^="write-addition-"][data-kb-entry]');
  await expect(card).toBeVisible();

  await card.locator('[contenteditable="true"]').click();
  await page.keyboard.type('Guyon is lame.');
  await page.getByTestId(proseTestIds.modeSource('kb-addition')).click();
  await expect(page.getByTestId(proseTestIds.sourceEditor('kb-addition'))).toHaveText(
    'Guyon is lame.',
  );
  await page.keyboard.press('Control+Home');
  await page.keyboard.type('Very. ');
  await card.getByTitle('Change entry').click();
  await page.keyboard.press('Escape');

  await expect.poll(() => patches).toEqual([{ body: 'Very. Guyon is lame.' }]);
});

test('Addition_WhenTheServerRefuses_ShouldLeaveNoCard', async ({ mount, page }) => {
  await stubKb(page, 'The yard was empty.\n');
  await page.route('**/api/scenes/*/kb-additions', (route) =>
    route.fulfill({
      status: 400,
      json: { error: 'Entry is outside the book.', code: 'validation' },
    }),
  );
  await openWrite(mount);

  await editor(page).locator('p').first().click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('codex-addition')).click();
  await page.getByTestId(writeTestIds.entryPickerOption(entryId)).click();

  await expect(page.getByTestId(proseTestIds.directiveCard('kb-addition'))).toHaveCount(0);
});
