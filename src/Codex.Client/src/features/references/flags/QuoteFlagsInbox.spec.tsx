import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../../AppRoutes';
import {
  stubQuoteFlags,
  stubQuoteFlagsApi,
  stubReferencesApi,
} from '../../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../../test-support/shellStubs';
import { refsTestIds } from '../testIds';

const book = allBooks(library)[0];
const route = `/books/${book.id}/references`;
const [quote, match] = stubQuoteFlags;

test('QuoteFlagsInbox_WhenFlagsExist_ShouldCountThemAndListOnOpen', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route } });

  await expect(page.getByTestId(refsTestIds.flagInbox)).toHaveText('2 possible unflagged quotes');
  await page.getByTestId(refsTestIds.flagInbox).click();

  const first = page.getByTestId(refsTestIds.flagItem(quote.id));
  await expect(first.getByTestId(refsTestIds.flagScene)).toHaveText('Chapter 6 · Scene 1');
  await expect(first.getByTestId(refsTestIds.flagExcerpt)).toHaveText(quote.excerpt);
  await expect(first.getByTestId(refsTestIds.flagCite)).toHaveText('Cite a source…');
  await expect(first.getByTestId(refsTestIds.flagCite)).toBeDisabled();
  await expect(first.getByTestId(refsTestIds.flagWrite)).toHaveAttribute(
    'href',
    `/books/${book.id}/write#scene=6.1`,
  );
  const second = page.getByTestId(refsTestIds.flagItem(match.id));
  await expect(second.getByTestId(refsTestIds.flagWhy)).toHaveText(match.why);
  await expect(second.getByTestId(refsTestIds.flagCite)).toHaveText('Cite Whitlock, 2021');
});

test('QuoteFlagsInbox_WhenNoFlags_ShouldShowNothing', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id, []);
  await mount(<AppRoutes />, { hooksConfig: { route } });

  await expect(page.getByTestId(refsTestIds.page)).toBeVisible();
  await expect(page.getByTestId(refsTestIds.flagInbox)).toHaveCount(0);
});

test('QuoteFlagsInbox_WhenNotAQuoteClicked_ShouldRemoveTheFlag', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route } });
  await page.getByTestId(refsTestIds.flagInbox).click();

  await page
    .getByTestId(refsTestIds.flagItem(quote.id))
    .getByTestId(refsTestIds.flagNotQuote)
    .click();

  await expect(page.getByTestId(refsTestIds.flagItem(quote.id))).toHaveCount(0);
  await expect(page.getByTestId(refsTestIds.flagInbox)).toHaveText('1 possible unflagged quote');
});

test('QuoteFlagsInbox_WhenLastFlagResolved_ShouldSaySoAndKeepThePanel', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id, [quote]);
  await mount(<AppRoutes />, { hooksConfig: { route } });
  await page.getByTestId(refsTestIds.flagInbox).click();

  await page.getByTestId(refsTestIds.flagNotQuote).click();

  await expect(page.getByTestId(refsTestIds.flagEmpty)).toBeVisible();
});

test('QuoteFlagsInbox_WhenOtherSourceNoted_ShouldSendTheNoteAndRemoveTheFlag', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route } });
  await page.getByTestId(refsTestIds.flagInbox).click();
  const card = page.getByTestId(refsTestIds.flagItem(match.id));
  await card.getByTestId(refsTestIds.flagOther).click();
  await expect(card.getByTestId(refsTestIds.flagOtherSubmit)).toBeDisabled();
  await card.getByTestId(refsTestIds.flagOtherNote).fill('Reinertsen, 2009');

  const request = page.waitForRequest((r) =>
    r.url().endsWith(`/quote-flags/${match.id}/other-source`),
  );
  await card.getByTestId(refsTestIds.flagOtherSubmit).click();

  expect((await request).postDataJSON()).toEqual({ note: 'Reinertsen, 2009' });
  await expect(page.getByTestId(refsTestIds.flagItem(match.id))).toHaveCount(0);
});

test('QuoteFlagsInbox_WhenTheWriteFails_ShouldKeepTheFlagAndSayWhy', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await page.route('**/api/quote-flags/*/not-a-quote', (r) =>
    r.fulfill({
      status: 409,
      json: { code: 'conflict', error: 'This quote has already been resolved.' },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route } });
  await page.getByTestId(refsTestIds.flagInbox).click();
  const card = page.getByTestId(refsTestIds.flagItem(quote.id));

  await card.getByTestId(refsTestIds.flagNotQuote).click();

  await expect(card.getByTestId(refsTestIds.flagItemError)).toHaveText(
    'This quote has already been resolved.',
  );
  await expect(card).toBeVisible();
});

test('QuoteFlagCard_WhenNoSourceWasGuessed_ShouldKeepCiteDisabledAndSayWhy', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route } });
  await page.getByTestId(refsTestIds.flagInbox).click();

  const cite = page.getByTestId(refsTestIds.flagItem(quote.id)).getByTestId(refsTestIds.flagCite);
  await expect(cite).toBeDisabled();
  await expect(cite).toHaveAttribute(
    'title',
    'No source was matched. Select the words in Write and cite a source there.',
  );
  await expect(
    page.getByTestId(refsTestIds.flagItem(match.id)).getByTestId(refsTestIds.flagCite),
  ).toBeEnabled();
});
