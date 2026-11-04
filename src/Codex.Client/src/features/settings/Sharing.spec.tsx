import { expect, test } from '@playwright/experimental-ct-react';
import type { Locator, Page } from 'playwright';
import libraryFixture from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type {
  BookSettings,
  PublicationSettings,
  SeriesMovePreview,
} from '@codex/core/api/settings';
import type { Library, Sharing, SharingUser } from '@codex/core/api/types';
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { stubShellApi } from '../../test-support/shellStubs';
import { settingsTestIds as ids } from './testIds';

/** The option labels of a select, read through its test id. */
const optionsOf = (select: Locator) =>
  select.evaluate((el) => Array.from((el as HTMLSelectElement).options, (o) => o.text));

// Book settings → Sharing and the page by role (#80). The header's role (from the Library fixture's book) decides
// what is editable; GET …/sharing fills the card.

const book = '0199a000-0000-7000-8000-000000000012';
const seriesId = '0199c0de-0000-7000-8000-000200000001';
const other = '0199a000-0000-7000-8000-0000000000a2';

const jd: SharingUser = {
  id: '0199c0de-0000-7000-8000-000100000001',
  displayName: 'Joel Dickson',
  email: 'jd@codex.local',
};
const ana: SharingUser = {
  id: '0199c0de-0000-7000-8000-000100000002',
  displayName: 'Ana Kowalczyk',
  email: 'ana@codex.local',
};
const bo: SharingUser = {
  id: '0199c0de-0000-7000-8000-0000000000b4',
  displayName: 'Bo Lindqvist',
  email: 'bo@example.test',
};
const cy: SharingUser = {
  id: '0199c0de-0000-7000-8000-0000000000c5',
  displayName: 'Cy Moreau',
  email: 'cy@example.test',
};
const dee: SharingUser = {
  id: '0199c0de-0000-7000-8000-0000000000d6',
  displayName: 'Dee Okafor',
  email: 'dee@example.test',
};

const settings: BookSettings = {
  id: book,
  title: 'Hymns for Machines',
  authorName: 'Joel Dickson',
  status: 'writing',
  targetWords: 40000,
  wordCount: 10000,
  progressPercent: 25,
  seriesId,
  seriesName: 'The Forsaken Gods',
  seriesNumber: 1,
  seriesOptions: [
    { id: seriesId, name: 'The Forsaken Gods', bookCount: 1 },
    { id: other, name: 'Other Cycle', bookCount: 1 },
  ],
  seriesOrder: [{ id: book, title: 'Hymns for Machines', number: 1, status: 'writing' }],
  styleGuide: 'Close third.',
  seriesGuide: null,
  archived: false,
  subtitle: null,
  description: null,
};
const standalone: BookSettings = {
  ...settings,
  seriesId: null,
  seriesName: null,
  seriesNumber: null,
  seriesOrder: [],
};

const publication: PublicationSettings = {
  bookId: book,
  seriesName: null,
  style: { value: 'chicago', inherited: false },
  rulesName: 'Default',
  quoteWordLimit: { value: 300, inherited: false },
  publisher: { value: null, inherited: false },
  plannedYear: { value: null, inherited: false },
  formats: { value: [], inherited: false },
  printRun: { value: null, inherited: false },
  territory: { value: null, inherited: false },
};

const ownerSeries: Sharing = {
  owner: jd,
  role: 'owner',
  book: [{ user: ana, role: 'view' }],
  series: {
    id: seriesId,
    title: 'The Forsaken Gods',
    grants: [
      { user: bo, role: 'write' },
      { user: ana, role: 'write' },
      { user: cy, role: 'view' },
    ],
  },
};
const ownerAlone: Sharing = { owner: jd, role: 'owner', book: [], series: null };

/** The Library fixture with Hymns for Machines at `role`, owned by Joel Dickson. */
function libraryAs(role: string): Library {
  const library = structuredClone(libraryFixture) as Library;
  for (const s of library.series) {
    for (const b of s.books) if (b.id === book) b.role = role;
  }
  return library;
}

interface Calls {
  lookups: string[];
  puts: { url: string; body: unknown }[];
  deletes: string[];
}

interface StubOptions {
  role?: string;
  book?: BookSettings;
  sharing?: Sharing;
  me?: SharingUser;
  users?: SharingUser[];
}

async function stub(page: Page, options: StubOptions = {}): Promise<Calls> {
  const calls: Calls = { lookups: [], puts: [], deletes: [] };
  const users = options.users ?? [jd, ana, bo, cy, dee];
  await stubShellApi(page, libraryAs(options.role ?? 'owner'));
  const me = options.me ?? jd;
  await page.route('**/api/auth/me', (route) =>
    route.fulfill({
      json: { ...me, initials: 'X', isAdmin: false, mustChangePassword: false },
    }),
  );
  await page.route(`**/api/books/${book}/settings`, (route) =>
    route.fulfill({ json: options.book ?? settings }),
  );
  await page.route(`**/api/books/${book}/publication-settings`, (route) =>
    route.fulfill({ json: publication }),
  );
  await page.route(`**/api/books/${book}/settings/rules-impact**`, (route) =>
    route.fulfill({ json: { limit: 300, riskCount: 0, sources: [] } }),
  );
  await page.route(`**/api/books/${book}/settings/layout`, (route) =>
    route.fulfill({ json: { template: null, riskCount: 0 } }),
  );
  await page.route(`**/api/books/${book}/sharing`, (route) =>
    route.fulfill({ json: options.sharing ?? ownerSeries }),
  );
  await page.route('**/api/books/*/sharing/*', (route) => {
    const request = route.request();
    if (request.method() === 'DELETE') {
      calls.deletes.push(request.url());
      return route.fulfill({ status: 204 });
    }
    calls.puts.push({ url: request.url(), body: request.postDataJSON() });
    return route.fulfill({ json: { user: ana, ...(request.postDataJSON() as object) } });
  });
  await page.route(`**/api/books/${book}/sharing/lookup`, (route) => {
    const { email } = route.request().postDataJSON() as { email: string };
    calls.lookups.push(email);
    if (email === 'busy@example.test') {
      return route.fulfill({
        status: 429,
        json: { error: 'Too many lookups.', code: 'rate_limited' },
      });
    }
    const found = users.find((u) => u.email === email.trim().toLowerCase());
    return found
      ? route.fulfill({ json: found })
      : route.fulfill({
          status: 404,
          json: { error: 'No user has that email.', code: 'not_found' },
        });
  });
  return calls;
}

const open = (mount: Parameters<Parameters<typeof test>[2]>[0]['mount']) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book}/settings` } });

const urlFor = (target: string, user: SharingUser) =>
  expect.stringContaining(`/api/books/${target}/sharing/${user.id}`);

test('Sharing_WhenStandaloneAndUnshared_ShouldShowTheOwnerAndOnlyYou', async ({ mount, page }) => {
  await stub(page, { book: standalone, sharing: ownerAlone });
  await open(mount);

  const card = page.getByTestId(ids.sharing);
  await expect(card.getByTestId(ids.sharingOwner)).toContainText('You· Owner');
  await expect(card.getByTestId(ids.sharingOwner)).toContainText('jd@codex.local');
  await expect(card.getByTestId(ids.sharingEmpty)).toHaveText('Only you can open this book.');
  await expect(card.getByTestId(ids.sharingBookList)).toHaveCount(0);
  await expect(page.getByLabel('Add people')).toBeVisible();
  await expect(page.getByTestId(ids.navItem('sharing'))).toBeVisible();
  await expect(page.getByTestId(ids.sharingCount)).toHaveCount(0);
});

test('Sharing_WhenInASeries_ShouldListThisBookAndTheWholeSeries', async ({ mount, page }) => {
  await stub(page);
  await open(mount);

  const bookList = page.getByTestId(ids.sharingBookList);
  const seriesList = page.getByTestId(ids.sharingSeriesList);
  await expect(bookList).toContainText('This book');
  await expect(bookList).toContainText(
    'People who see Hymns for Machines and nothing else in the series.',
  );
  await expect(bookList.getByTestId(ids.sharingPerson)).toHaveCount(1);
  await expect(bookList.getByTestId(ids.sharingPerson)).toContainText(
    'ana@codex.local · Can edit through the series',
  );
  await expect(seriesList).toContainText('Whole series: The Forsaken Gods');
  await expect(seriesList).toContainText('You can change this from any book in the series.');
  await expect(seriesList.getByTestId(ids.sharingPerson)).toHaveCount(3);
  await expect(page.getByLabel('Add people to Hymns for Machines only')).toBeVisible();
  await expect(page.getByLabel('Add people to every book in The Forsaken Gods')).toBeVisible();
  // Ana is on both lists: three people besides the owner.
  await expect(page.getByTestId(ids.sharingCount)).toHaveText('3');
});

test('Sharing_WhenARoleChangesAndIsUndone_ShouldSaveEachAtOnce', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  const row = page.getByTestId(ids.sharingBookList).getByTestId(ids.sharingPerson);
  await row.getByTestId(ids.sharingRole).selectOption('comment');

  await expect
    .poll(() => calls.puts)
    .toEqual([{ url: urlFor(book, ana), body: { role: 'comment' } }]);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Ana Kowalczyk can now comment Hymns for Machines',
  );
  await page.getByTestId(editingTestIds.toastUndo).click();
  await expect
    .poll(() => calls.puts.at(-1))
    .toEqual({
      url: urlFor(book, ana),
      body: { role: 'view' },
    });
});

test('Sharing_WhenRemovedFromTheSeries_ShouldDeleteTheSeriesGrant', async ({ mount, page }) => {
  const calls = await stub(page);
  await open(mount);

  const boRow = page
    .getByTestId(ids.sharingSeriesList)
    .getByTestId(ids.sharingPerson)
    .filter({ hasText: 'Bo Lindqvist' });
  await boRow.getByTestId(ids.sharingRemove).click();

  await expect.poll(() => calls.deletes).toEqual([urlFor(seriesId, bo)]);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Removed Bo Lindqvist from The Forsaken Gods. Their open tabs go back to Library.',
  );
});

test('Lookup_WhenTheAddressIsIncompleteOrANearMiss_ShouldFindNobody', async ({ mount, page }) => {
  const calls = await stub(page, { book: standalone, sharing: ownerAlone });
  await open(mount);
  const email = page.getByTestId(ids.sharingEmail);

  await email.fill('ana@codex');
  await expect(page.getByTestId(ids.sharingNoMatch)).toHaveCount(0);
  await expect(page.getByTestId(ids.sharingMatch)).toHaveCount(0);

  await email.fill('ana@codex.co');
  await expect(page.getByTestId(ids.sharingNoMatch)).toHaveText('No Codex user has that email.');
  // Only the complete address was looked up, once.
  expect(calls.lookups).toEqual(['ana@codex.co']);
});

test('Lookup_WhenTheUserIsDisabled_ShouldSayNoMatch', async ({ mount, page }) => {
  // A disabled user is a 404 like any other unknown address.
  await stub(page, { book: standalone, sharing: ownerAlone, users: [jd, ana] });
  await open(mount);

  await page.getByTestId(ids.sharingEmail).fill('dee@example.test');
  await expect(page.getByTestId(ids.sharingNoMatch)).toBeVisible();
});

test('Lookup_WhenAWholeAddressMatches_ShouldAddThePersonAtTheChosenRole', async ({
  mount,
  page,
}) => {
  const calls = await stub(page, { book: standalone, sharing: ownerAlone });
  await open(mount);

  await page.getByTestId(ids.sharingEmail).fill('  Ana@Codex.Local ');
  const match = page.getByTestId(ids.sharingMatch);
  await expect(match).toContainText('Ana Kowalczyk');
  await expect(match).toContainText('ana@codex.local');
  await expect(match.getByTestId(ids.sharingMatchRole)).toHaveValue('view');
  await match.getByTestId(ids.sharingMatchRole).selectOption('comment');
  await match.getByTestId(ids.sharingAdd).click();

  await expect
    .poll(() => calls.puts)
    .toEqual([{ url: urlFor(book, ana), body: { role: 'comment' } }]);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText(
    'Shared Hymns for Machines with Ana Kowalczyk · Can comment',
  );
  await expect(page.getByTestId(ids.sharingEmail)).toHaveValue('');
});

test('Lookup_WhenTheAddressIsYoursOrAlreadyShared_ShouldSaySo', async ({ mount, page }) => {
  await stub(page);
  await open(mount);
  const bookList = page.getByTestId(ids.sharingBookList);
  const seriesList = page.getByTestId(ids.sharingSeriesList);

  await bookList.getByTestId(ids.sharingEmail).fill('jd@codex.local');
  await expect(bookList.getByTestId(ids.sharingMessage)).toHaveText(
    'That’s you. You own this book.',
  );
  await bookList.getByTestId(ids.sharingEmail).fill('ana@codex.local');
  await expect(bookList.getByTestId(ids.sharingMessage)).toHaveText(
    'Ana Kowalczyk already has Can view on this book. Change it in the list above.',
  );
  await seriesList.getByTestId(ids.sharingEmail).fill('cy@example.test');
  await expect(seriesList.getByTestId(ids.sharingMessage)).toHaveText(
    'Cy Moreau already has Can view on every book in The Forsaken Gods. Change it in the list above.',
  );
});

test('Lookup_WhenTheSeriesAlreadyCoversThePerson_ShouldOnlyOfferMore', async ({ mount, page }) => {
  await stub(page);
  await open(mount);
  const bookList = page.getByTestId(ids.sharingBookList);
  const email = bookList.getByTestId(ids.sharingEmail);

  await email.fill('bo@example.test');
  await expect(bookList.getByTestId(ids.sharingMessage)).toHaveText(
    'Bo Lindqvist can already edit every book in The Forsaken Gods. A grant on one book can only add access.',
  );
  await expect(bookList.getByTestId(ids.sharingAdd)).toHaveCount(0);

  await email.fill('cy@example.test');
  const match = page.getByTestId(ids.sharingMatch);
  await expect
    .poll(() => optionsOf(match.getByTestId(ids.sharingMatchRole)))
    .toEqual(['Can comment', 'Can edit']);
  await expect(page.getByTestId(ids.sharingMatchFloor)).toHaveText(
    'Cy Moreau has Can view through The Forsaken Gods already. Pick a higher role for this book.',
  );
});

test('Lookup_WhenRateLimited_ShouldSayToWait', async ({ mount, page }) => {
  await stub(page, { book: standalone, sharing: ownerAlone });
  await open(mount);

  await page.getByTestId(ids.sharingEmail).fill('busy@example.test');
  await expect(page.getByTestId(ids.sharingMessage)).toContainText('Wait a minute');
});

test('Sharing_WhenACommenterOpensIt_ShouldReadTheListAndThePage', async ({ mount, page }) => {
  const readOnly: Sharing = {
    owner: { ...jd, email: null },
    role: 'comment',
    book: [
      { user: { ...ana, email: null }, role: 'comment' },
      { user: { ...bo, email: null }, role: 'view' },
    ],
    series: null,
  };
  await stub(page, { role: 'comment', book: standalone, sharing: readOnly, me: ana });
  await open(mount);

  await expect(page.getByTestId(ids.readOnlyNote)).toHaveText(
    'Comment only. Joel Dickson shared Hymns for Machines with you to read and comment on. These are its settings as they are now; ask Joel Dickson for edit access to change them.',
  );
  await expect(page.getByTestId(ids.card('sharing'))).toContainText(
    'Who can open this book. Only Joel Dickson, the owner, can change this.',
  );
  const card = page.getByTestId(ids.sharing);
  await expect(card.getByTestId(ids.sharingOwner)).toContainText('Joel Dickson· Owner');
  const mine = card.getByTestId(ids.sharingPerson).filter({ hasText: 'Ana Kowalczyk' });
  await expect(mine.getByTestId(ids.sharingYou)).toBeVisible();
  await expect(mine.getByTestId(ids.sharingRoleText)).toHaveText('Can comment');
  await expect(card).not.toContainText('@');
  await expect(card.getByTestId(ids.sharingRole)).toHaveCount(0);
  await expect(card.getByTestId(ids.sharingRemove)).toHaveCount(0);
  await expect(card.getByTestId(ids.sharingEmail)).toHaveCount(0);
  await expect(page.getByTestId(ids.title)).toBeDisabled();
  await expect(page.getByTestId(ids.navFoot)).toHaveText(
    'You can read these settings. Joel Dickson owns this book.',
  );
});

test('Settings_WhenAViewerOpensIt_ShouldDisableEveryControl', async ({ mount, page }) => {
  const readOnly: Sharing = {
    owner: { ...jd, email: null },
    role: 'view',
    book: [{ user: { ...ana, email: null }, role: 'view' }],
    series: null,
  };
  await stub(page, { role: 'view', sharing: readOnly, me: ana });
  await open(mount);

  await expect(page.getByTestId(ids.readOnlyNote)).toContainText('View only.');
  await expect(page.getByTestId(ids.readOnlyNote)).toContainText('with you to read.');
  await expect(page.getByTestId(ids.title)).toBeDisabled();
  await expect(page.getByTestId(ids.title)).toHaveValue('Hymns for Machines');
  await expect(page.getByTestId(ids.statusOption('draft'))).toBeDisabled();
  await expect(page.getByTestId(ids.guide)).toHaveAttribute('aria-disabled', 'true');
  await expect
    .poll(() =>
      page
        .getByTestId(ids.guide)
        .evaluate((el) => el.querySelector('[contenteditable="true"], textarea:enabled') === null),
    )
    .toBe(true);
  await expect(page.getByTestId(ids.exportSource)).toBeDisabled();
  await expect(page.getByTestId(ids.card('archive'))).toContainText(
    'Only Joel Dickson, the owner, can duplicate or archive it, and exporting needs edit access.',
  );
  await expect(page.getByTestId(editingTestIds.saveStatus)).toHaveCount(0);
});

test('Settings_WhenAnEditorOpensIt_ShouldKeepOwnerOnlyActionsOff', async ({ mount, page }) => {
  const forEditor: Sharing = {
    owner: { ...jd, email: null },
    role: 'write',
    book: [],
    series: {
      id: seriesId,
      title: 'The Forsaken Gods',
      grants: [{ user: { ...ana, email: null }, role: 'write' }],
    },
  };
  await stub(page, { role: 'write', sharing: forEditor, me: ana });
  await open(mount);

  await expect(page.getByTestId(ids.readOnlyNote)).toHaveCount(0);
  await expect(page.getByTestId(ids.title)).toBeEnabled();
  await expect(page.getByTestId(ids.seriesSelect)).toBeDisabled();
  await expect(page.getByTestId(ids.numberSelect)).toBeDisabled();
  await expect(page.getByTestId(ids.ownerOnly)).toHaveText(
    'Only Joel Dickson, the owner, can move this book or change its number.',
  );
  await expect(page.getByTestId(ids.statusOption('draft'))).toBeEnabled();
  await expect(page.getByTestId(ids.statusOption('published'))).toBeDisabled();
  await expect(page.getByTestId(ids.statusOption('published'))).toHaveAttribute(
    'title',
    'Only Joel Dickson, the owner, can publish this book or start a new edition',
  );
  await expect(page.getByTestId(ids.duplicate)).toBeDisabled();
  await expect(page.getByTestId(ids.newEdition)).toBeDisabled();
  await expect(page.getByTestId(ids.archive)).toBeDisabled();
  await expect(page.getByTestId(ids.exportSource)).toBeEnabled();
  await expect(page.getByTestId(ids.sharingEmail)).toHaveCount(0);
  await expect(page.getByTestId(ids.navFoot)).toHaveText(
    'Changes save as you go. Joel Dickson owns this book, so sharing, moving and archiving are theirs.',
  );
});

test('Move_WhenTheSeriesChangesWhoCanSee_ShouldSayWhoGainsAndLoses', async ({ mount, page }) => {
  await stub(page);
  const preview: SeriesMovePreview = {
    bookId: book,
    title: 'Hymns for Machines',
    from: { id: seriesId, name: 'The Forsaken Gods', number: 1 },
    to: { id: other, name: 'Other Cycle', number: 2 },
    steps: [],
    notes: [],
    warnings: [],
    targetSeriesSharedWith: 3,
    losingAccess: 1,
  };
  await page.route(`**/api/books/${book}/series-move/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await open(mount);

  await page.getByTestId(ids.seriesSelect).selectOption(other);
  await page.getByTestId(ids.moveReview).click();

  await expect(page.getByTestId(ids.moveGain)).toHaveText(
    '3 people with access to Other Cycle will be able to see this book You can change this in Sharing.',
  );
  await expect(page.getByTestId(ids.moveLose)).toHaveText(
    '1 person with access to The Forsaken Gods will no longer see this book People you shared this book with directly keep their access.',
  );
});

test('Archive_WhenSharedDirectly_ShouldNameWhoLosesAccess', async ({ mount, page }) => {
  await stub(page, {
    sharing: {
      ...ownerSeries,
      book: [
        { user: ana, role: 'view' },
        { user: dee, role: 'write' },
      ],
    },
  });
  await open(mount);

  await expect(page.getByTestId(ids.archiveLoss)).toHaveText(
    '2 people you shared it with directly lose access, and Restore doesn’t share it again.',
  );
  await page.getByTestId(ids.archive).click();
  await expect(page.getByTestId(ids.archiveDialogLoss)).toHaveText(
    'Ana Kowalczyk and Dee Okafor lose access. Restoring the book doesn’t share it again.',
  );
});
