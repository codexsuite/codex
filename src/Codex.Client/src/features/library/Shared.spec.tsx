import { expect, test } from '@playwright/experimental-ct-react';
import type { Locator } from 'playwright';
import libraryFixture from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { Library, LibraryBook, LibrarySeries } from '@codex/core/api/types';
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { seriesTestIds } from '../series/testIds';
import { libraryTestIds as ids } from './testIds';

/** The option labels of a select, read through its test id. */
const optionsOf = (select: Locator) =>
  select.evaluate((el) => Array.from((el as HTMLSelectElement).options, (o) => o.text));

// Books shared with you on the Library and Series screens (#80).

const fixture = libraryFixture as Library;
const mara = { id: '0199c0de-0000-7000-8000-0000000000e1', displayName: 'Mara Lind' };
const template = fixture.standalone[0];

const book = (id: string, title: string, role: string, extra: Partial<LibraryBook> = {}) => ({
  ...template,
  id,
  title,
  role,
  owner: mara,
  ...extra,
});

const lanternId = '0199a000-0000-7000-8000-0000000000c1';
const lamplighter = book(
  '0199a000-0000-7000-8000-0000000000c2',
  "The Lamplighter's Debt",
  'write',
  {
    seriesId: lanternId,
    seriesName: 'The Lantern Archive',
    seriesNumber: 1,
  },
);
const wick = book('0199a000-0000-7000-8000-0000000000c3', 'Wick and Ember', 'write', {
  seriesId: lanternId,
  seriesName: 'The Lantern Archive',
  seriesNumber: 2,
});
const lantern: LibrarySeries = {
  ...fixture.series[0],
  id: lanternId,
  name: 'The Lantern Archive',
  bookCount: 2,
  books: [lamplighter, wick],
};
const orchard = book('0199a000-0000-7000-8000-0000000000c4', 'Glass Orchard', 'view');
const northbound = fixture.standalone[0];
const own = fixture.series[0];

const shared: Library = {
  series: [own, lantern],
  standalone: [northbound, orchard],
};
const nothingShared: Library = { series: [own], standalone: [] };

test('Shelf_WhenBooksAreShared_ShouldShowWhoSharedThemAndYourRole', async ({ mount, page }) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });

  const orchardTile = page.getByTestId(testIds.shell.tile(orchard.id));
  await expect(orchardTile.getByTestId(ids.sharedBy)).toHaveText('Shared by Mara Lind');
  await expect(orchardTile.getByTestId(ids.roleChip)).toHaveText('View only');
  await expect(orchardTile.getByTestId(ids.roleChip)).toHaveAttribute(
    'title',
    'You can read it. Typing and changes are turned off.',
  );
  const northTile = page.getByTestId(testIds.shell.tile(northbound.id));
  await expect(northTile.getByTestId(ids.sharedBy)).toHaveText('Shared by Ana Kowalczyk');
  await expect(northTile.getByTestId(ids.roleChip)).toHaveText('Edit');
  const stack = page.getByTestId(testIds.shell.seriesStack(lanternId));
  await expect(stack.getByTestId(ids.sharedBy)).toHaveText('Shared by Mara Lind');
  await expect(stack.getByTestId(ids.roleChip)).toHaveText('Edit');
  await expect(
    page.getByTestId(testIds.shell.seriesStack(own.id)).getByTestId(ids.roleChip),
  ).toHaveCount(0);
});

test('List_WhenBooksAreShared_ShouldShowTheOwnerColumnAndChip', async ({ mount, page }) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?view=list' } });

  const ownRow = page.getByTestId(testIds.shell.listRow(own.books[0].id));
  await expect(ownRow.getByTestId(ids.ownerCell)).toHaveText('You');
  await expect(ownRow.getByTestId(ids.roleChip)).toHaveCount(0);
  const orchardRow = page.getByTestId(testIds.shell.listRow(orchard.id));
  await expect(orchardRow.getByTestId(ids.ownerCell)).toHaveText('Mara Lind');
  await expect(orchardRow.getByTestId(ids.roleChip)).toHaveText('View only');
  await expect(page.getByTestId(ids.groupRow).filter({ hasText: 'Lantern' })).toHaveText(
    'The Lantern Archive · 2 · Shared by Mara Lind',
  );
});

test('Filter_WhenSharedWithMeOrMine_ShouldNarrowTheShelfAndKeepItInTheHash', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });

  await expect(page.getByTestId(ids.filterOption('all'))).toHaveAttribute('aria-pressed', 'true');
  await page.getByTestId(ids.filterOption('shared')).click();
  await expect(page.getByTestId(ids.filterOption('shared'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByTestId(testIds.shell.seriesStack(own.id))).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.seriesStack(lanternId))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(orchard.id))).toBeVisible();
  await expect(page.getByTestId(ids.addTile)).toHaveCount(0);

  await page.getByTestId(ids.filterOption('mine')).click();
  await expect(page.getByTestId(testIds.shell.seriesStack(own.id))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.seriesStack(lanternId))).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.tile(orchard.id))).toHaveCount(0);
});

test('Filter_WhenOpenedFromTheHash_ShouldStartFiltered', async ({ mount, page }) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/#filter=shared' } });

  await expect(page.getByTestId(ids.filterOption('shared'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByTestId(testIds.shell.seriesStack(own.id))).toHaveCount(0);
});

test('Filter_WhenNothingIsShared_ShouldSaySo', async ({ mount, page }) => {
  await stubShellApi(page, nothingShared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/#filter=shared' } });

  await expect(page.getByTestId(ids.sharedEmpty)).toHaveText(
    'Nothing is shared with you yet.When someone shares a book or a series with you, it shows up here.',
  );
  await expect(page.getByTestId(ids.noResults)).toHaveCount(0);
});

test('SeriesScreen_WhenSharedWithYou_ShouldNameTheOwnerAndHideOwnerActions', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${lanternId}` } });

  await expect(page.getByTestId(ids.seriesShared)).toContainText('Shared by Mara Lind');
  await expect(page.getByTestId(ids.seriesShared).getByTestId(ids.roleChip)).toHaveText('Edit');
  await expect(page.getByTestId(seriesTestIds.settingsButton)).toHaveCount(0);
  await expect(page.getByTestId(seriesTestIds.addBook)).toHaveCount(0);
  await expect(page.getByTestId(seriesTestIds.addTile)).toHaveCount(0);
  const tile = page.getByTestId(testIds.shell.tile(wick.id));
  await expect(tile.getByTestId(ids.roleChip)).toHaveText('Edit');
  await expect(tile.getByTestId(ids.sharedBy)).toHaveCount(0);
  await expect(page.getByTestId(ids.partlySharedNote)).toHaveCount(0);
});

test('SeriesScreen_WhenOnlyOneBookIsShared_ShouldSaySoAboveIt', async ({ mount, page }) => {
  const rites = '0199a000-0000-7000-8000-0000000000c5';
  const hearth = book('0199a000-0000-7000-8000-0000000000c6', 'Hearth Songs', 'comment', {
    seriesId: rites,
    seriesName: 'Northern Rites',
    seriesNumber: 2,
  });
  const partly: LibrarySeries = {
    ...lantern,
    id: rites,
    name: 'Northern Rites',
    bookCount: 1,
    books: [hearth],
    partlyShared: true,
  };
  await stubShellApi(page, { series: [own, partly], standalone: [] });
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${rites}` } });

  await expect(page.getByTestId(ids.partlySharedNote)).toHaveText(
    'Mara Lind shared one book in this series with you. Only that book appears here.',
  );
});

test('SeriesScreen_WhenYourOwn_ShouldKeepItsActions', async ({ mount, page }) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${own.id}` } });

  await expect(page.getByTestId(seriesTestIds.settingsButton)).toBeVisible();
  await expect(page.getByTestId(ids.seriesShared)).toHaveCount(0);
});

test('NewBook_WhenSeriesAreShared_ShouldOnlyOfferYourOwn', async ({ mount, page }) => {
  await stubShellApi(page, shared);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?book=new' } });

  await expect
    .poll(() => optionsOf(page.getByTestId(ids.newBookSeries)))
    .toEqual(['None (standalone)', own.name]);
});
