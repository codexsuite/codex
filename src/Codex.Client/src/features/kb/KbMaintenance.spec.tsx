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
import { kbTestIds } from './testIds';

const hymns = library.series[0].books[1].id;
const [ward, harbour] = kb.entries;
const people = kb.categories[0];
const wardEntry = { ...entry, id: ward.id, name: ward.name, categoryId: people.id, aliases: [] };

type Mount = (component: ReactElement, options?: object) => Promise<MountResult>;

async function openWindow(mount: Mount, page: Page) {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) => route.fulfill({ json: wardEntry }));
  await mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${hymns}/plan#kb=${ward.id}&entry=${ward.id}` } },
  );
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
}

const conflict = {
  error: '"Conway" is already a name or alias of Conway\'s Law.',
  code: 'conflict',
};

test('Alias_WhenAnotherEntryHasIt_ShouldHoldUntilAddAnyway', async ({ mount, page }) => {
  await openWindow(mount, page);
  const patches: unknown[] = [];
  await page.route('**/api/kb/entries/*', (route) => {
    if (route.request().method() !== 'PATCH') return route.fulfill({ json: wardEntry });
    const body = route.request().postDataJSON() as { aliases?: string[] };
    if (body.aliases === undefined) return route.fulfill({ json: wardEntry });
    patches.push(body);
    return patches.length === 1
      ? route.fulfill({ status: 409, json: conflict })
      : route.fulfill({ json: wardEntry });
  });
  await page.getByTestId(kbTestIds.addAlias).click();
  await page.getByTestId(kbTestIds.aliasInput).fill('Conway');
  await page.getByTestId(kbTestIds.aliasInput).press('Enter');
  await expect(page.getByTestId(kbTestIds.aliasError)).toContainText('already a name or alias');
  await page.getByTestId(kbTestIds.aliasAddAnyway).click();
  await expect.poll(() => patches.length).toBe(2);
  expect(patches).toEqual([
    { aliases: ['Conway'], allowDuplicate: false },
    { aliases: ['Conway'], allowDuplicate: true },
  ]);
});

test('Alias_WhenShortOrCommon_ShouldSaveWithAWarning', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*/alias-check*', (route) =>
    route.fulfill({ json: { collidesWith: null, matches: 14, warn: true } }),
  );
  await page.getByTestId(kbTestIds.addAlias).click();
  await page.getByTestId(kbTestIds.aliasInput).fill('law');
  const saved = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(kbTestIds.aliasInput).press('Enter');
  expect((await saved).postDataJSON()).toEqual({ aliases: ['law'], allowDuplicate: false });
  await expect(page.getByTestId(kbTestIds.aliasWarning)).toContainText(
    'Matches 14 other words in the book',
  );
});

test('Rename_ShouldPatchTheNameAndKeepTheOldOneAsAnAliasByDefault', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.entryName).fill(`${ward.name} (renamed)`);
  await expect(page.getByTestId(kbTestIds.renameKeepAlias)).toBeChecked();
  const saved = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(kbTestIds.entryName).press('Enter');
  expect((await saved).postDataJSON()).toEqual({
    name: `${ward.name} (renamed)`,
    keepOldName: true,
    allowDuplicate: false,
  });
});

test('Rename_WhenTheAliasIsUnticked_ShouldNotKeepTheOldName', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.entryName).fill(`${ward.name} (renamed)`);
  await page.getByTestId(kbTestIds.renameKeepAlias).uncheck();
  const saved = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(kbTestIds.entryName).focus();
  await page.getByTestId(kbTestIds.entryName).press('Enter');
  expect((await saved).postDataJSON()).toMatchObject({ keepOldName: false });
});

test('Rename_WhenAnotherEntryHasTheName_ShouldOfferRenameAnyway', async ({ mount, page }) => {
  await openWindow(mount, page);
  const bodies: unknown[] = [];
  await page.route('**/api/kb/entries/*', (route) => {
    if (route.request().method() !== 'PATCH') return route.fulfill({ json: wardEntry });
    const body = route.request().postDataJSON() as { name?: string };
    // The text parts autosave on the same blur; only requests that carry the name matter here.
    if (body.name !== "Conway's Law") return route.fulfill({ json: wardEntry });
    bodies.push(body);
    return bodies.length === 1
      ? route.fulfill({ status: 409, json: conflict })
      : route.fulfill({ json: wardEntry });
  });
  await page.getByTestId(kbTestIds.entryName).fill("Conway's Law");
  await page.getByTestId(kbTestIds.entryName).press('Enter');
  await expect(page.getByTestId(kbTestIds.entryError)).toContainText('already a name or alias');
  await page.getByTestId(kbTestIds.renameAnyway).click();
  await expect.poll(() => bodies.length).toBe(2);
  expect(bodies[1]).toMatchObject({ name: "Conway's Law", allowDuplicate: true });
});

test('Rename_WhenEmpty_ShouldShowTheErrorAndKeepTheName', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.getByTestId(kbTestIds.entryName).fill('  ');
  await page.getByTestId(kbTestIds.entryName).press('Enter');
  await expect(page.getByTestId(kbTestIds.entryError)).toBeVisible();
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(ward.name);
});

test('Merge_ShouldListWhatMovesThenPostTheMerge', async ({ mount, page }) => {
  await openWindow(mount, page);
  await page.route('**/api/kb/entries/*/merge-impact*', (route) =>
    route.fulfill({
      json: {
        intoName: harbour.name,
        sceneLinks: 1,
        additions: 0,
        related: 0,
        newAliases: [ward.name],
      },
    }),
  );
  await page.route('**/api/kb/entries/*/merge', (route) => route.fulfill({ json: wardEntry }));
  await page.getByTestId(kbTestIds.entryMenu).click();
  await page.getByTestId(kbTestIds.entryMenuMerge).click();
  await expect(page.getByTestId(kbTestIds.mergeOption(ward.id))).toHaveCount(0);
  await page.getByTestId(kbTestIds.mergeOption(harbour.id)).click();
  await expect(page.getByTestId(kbTestIds.mergeMessage)).toHaveText(
    `1 scene link, 0 additions, 0 related. "${ward.name}" becomes an alias.`,
  );
  const merged = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/merge'));
  await page.getByTestId(kbTestIds.mergeConfirm).click();
  expect((await merged).postDataJSON()).toEqual({ intoId: harbour.id });
});

test('Merge_WhenCancelled_ShouldWriteNothing', async ({ mount, page }) => {
  await openWindow(mount, page);
  let posted = false;
  await page.route('**/api/kb/entries/*/merge', (route) => {
    posted = true;
    return route.fulfill({ json: wardEntry });
  });
  await page.getByTestId(kbTestIds.entryMenu).click();
  await page.getByTestId(kbTestIds.entryMenuMerge).click();
  await page.getByTestId(kbTestIds.mergeCancel).click();
  await expect(page.getByTestId(kbTestIds.mergeDialog)).toHaveCount(0);
  expect(posted).toBe(false);
});
