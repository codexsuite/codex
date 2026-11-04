import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { writeTestIds } from './testIds';

// Write below the write role (#80): every part reads, nothing takes typing or offers a change.
const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const beatId = '0199a000-0000-7000-8000-0000000000f1';
const beat = {
  id: beatId,
  sceneId: first.id,
  body: 'Guyon crosses.',
  targetWordCount: 400,
  isHidden: false,
  contextEntryIds: [] as string[],
  title: 'The crossing',
};
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };

// The screen's reads, at `role`; returns every write the page sent.
async function stubWrite(page: Page, role: string) {
  const writes: string[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({ json: { ...headerFor(book), role, owner } }),
  );
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [beat] } }),
  );
  await page.route(/\/api\/(scenes|beats|blocks)\//, (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().url());
    return route.fulfill({ status: 403, json: { error: 'Read only.', code: 'forbidden' } });
  });
  return writes;
}

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);

for (const [role, label, why] of [
  ['view', 'View only', 'Mara Lind shared this book with you to read.'],
  ['comment', 'Comment only', 'Ask Mara Lind for edit access.'],
] as const) {
  test(`RoleChip_WhenTheRoleIs${role}_ShouldExplainWhyTypingDoesNothing`, async ({
    mount,
    page,
  }) => {
    await stubWrite(page, role);
    await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

    const chip = page.getByTestId(testIds.shell.roleChip);
    await expect(chip).toHaveText(label);
    await expect(chip).toHaveAttribute('title', new RegExp(why));
    // The chip takes the save status's place.
    await expect(page.getByTestId(testIds.shell.saveStatusSlot)).toHaveCount(0);
  });
}

test('RoleChip_WhenTheRoleIsWrite_ShouldNotShow', async ({ mount, page }) => {
  await stubWrite(page, 'write');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(editor(page)).toHaveAttribute('contenteditable', 'true');
  await expect(page.getByTestId(testIds.shell.saveStatusSlot)).toHaveCount(1);
  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveCount(0);
});

test('Write_WhenViewOnly_ShouldReadButTakeNoTypingOrChanges', async ({ mount, page }) => {
  const writes = await stubWrite(page, 'view');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(editor(page)).toHaveAttribute('contenteditable', 'false');
  await editor(page).locator('p').first().click();
  await page.keyboard.type('Typed by a viewer.');
  await expect(editor(page)).not.toContainText('Typed by a viewer.');

  // The beat reads, without its grip, controls or Generate.
  await expect(page.getByTestId(writeTestIds.beatTitle(beatId))).toHaveAttribute('readonly', '');
  await expect(page.getByTestId(writeTestIds.beatBody(beatId))).toHaveAttribute(
    'contenteditable',
    'false',
  );
  for (const id of [
    writeTestIds.beatGrip(beatId),
    writeTestIds.beatHide(beatId),
    writeTestIds.beatMore(beatId),
    writeTestIds.beatGenerate(beatId),
    writeTestIds.beatContextAdd(beatId),
    writeTestIds.beatClear(beatId),
    writeTestIds.sceneLabelAdd(first.id),
    writeTestIds.sceneEntryAdd(first.id),
    writeTestIds.replaceToggle,
  ]) {
    await expect(page.getByTestId(id)).toHaveCount(0);
  }
  // The gutter's summary reads only.
  await expect(
    page.getByTestId(writeTestIds.sceneSummary(first.id)).locator('[contenteditable]').first(),
  ).toHaveAttribute('contenteditable', 'false');

  // Find still finds the prose.
  await page.getByTestId(writeTestIds.find).fill('Guyon crossed');
  await expect(page.getByTestId(writeTestIds.findCount)).not.toHaveText('0');
  expect(writes).toEqual([]);
});
