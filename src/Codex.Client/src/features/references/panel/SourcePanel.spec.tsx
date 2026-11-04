import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../../AppRoutes';
import { stubDetail, stubReferencesApi, stubSources } from '../../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../../test-support/shellStubs';
import { refsTestIds } from '../testIds';

const book = allBooks(library)[0];
const needsCheck = stubSources[1];
const risk = stubSources[2];
const route = (source: string) => `/books/${book.id}/references#${source}`;

async function open(
  mount: (node: React.JSX.Element, options: object) => Promise<unknown>,
  page: Page,
  source: string,
  setup?: () => Promise<unknown>,
) {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await setup?.();
  await mount(<AppRoutes />, { hooksConfig: { route: route(source) } });
}

test('SourcePanel_WhenSourceSelected_ShouldShowVerdictChecklistUsesAndHistory', async ({
  mount,
  page,
}) => {
  await open(mount, page, needsCheck.id);

  await expect(page.getByTestId(refsTestIds.panelTitle)).toHaveText(needsCheck.title);
  await expect(page.getByTestId(refsTestIds.verdictStatus)).toHaveText('Needs check');
  await expect(page.getByTestId(refsTestIds.verdictAction)).toHaveText('Mark as cleared');
  await expect(page.locator('[data-testid^="refs-check-"]')).toHaveCount(2);
  await expect(
    page.getByTestId(refsTestIds.use('bbbbbbbb-0000-4000-8000-000000000001')),
  ).toContainText('Chapter 4 · Scene 1');
  await expect(page.getByTestId(refsTestIds.historyItem)).toHaveCount(1);
});

test('SourcePanel_WhenOpenInWriteShown_ShouldLinkToTheSceneAndSource', async ({ mount, page }) => {
  await open(mount, page, needsCheck.id);

  await expect(
    page.getByTestId(refsTestIds.useWrite('bbbbbbbb-0000-4000-8000-000000000001')),
  ).toHaveAttribute('href', `/books/${book.id}/write#scene=4.1&ref=${needsCheck.id}`);
});

test('SourcePanel_WhenTheUseIsInAnUnnumberedChapter_ShouldLinkToTheBareSceneNumber', async ({
  mount,
  page,
}) => {
  const useId = 'bbbbbbbb-0000-4000-8000-000000000001';
  await open(mount, page, needsCheck.id, () =>
    page.route(`**/api/references/${needsCheck.id}`, (route) =>
      route.fulfill({
        json: stubDetail(needsCheck, {
          bookId: book.id,
          uses: [{ ...stubDetail(needsCheck).uses[0], chapterNumber: null, sceneNumber: 3 }],
        }),
      }),
    ),
  );

  await expect(page.getByTestId(refsTestIds.useWrite(useId))).toHaveAttribute(
    'href',
    `/books/${book.id}/write#scene=3&ref=${needsCheck.id}`,
  );
});

test('SourcePanel_WhenSignedOff_ShouldPostShowClearedAndRefreshTheBookBar', async ({
  mount,
  page,
}) => {
  const cleared = stubDetail(needsCheck, {
    verdict: {
      status: 'cleared',
      reason: 'signed_off',
      action: 'reopen',
      signedOffBy: 'Joel Dickson',
      signedOffLabel: 'just now',
    },
  });
  await page.route(`**/api/references/${needsCheck.id}/sign-off`, (route) =>
    route.fulfill({ json: cleared }),
  );
  await open(mount, page, needsCheck.id);
  await expect(page.getByTestId(refsTestIds.verdictAction)).toBeVisible();

  const refetched = page.waitForRequest(
    (request) => request.url().endsWith(`/api/books/${book.id}`) && request.method() === 'GET',
  );
  await page.getByTestId(refsTestIds.verdictAction).click();

  await expect(page.getByTestId(refsTestIds.verdictStatus)).toHaveText('Cleared');
  await expect(page.getByTestId(refsTestIds.signedOff)).toContainText('Joel Dickson');
  await refetched;
});

test('SourcePanel_WhenSignOffIsRefused_ShouldShowTheReasonAndKeepTheVerdict', async ({
  mount,
  page,
}) => {
  await page.route(`**/api/references/${needsCheck.id}/sign-off`, (route) =>
    route.fulfill({
      status: 422,
      json: { code: 'blocked', error: 'A copyright risk is open on this source.' },
    }),
  );
  await open(mount, page, needsCheck.id);

  await page.getByTestId(refsTestIds.verdictAction).click();

  await expect(page.getByTestId(refsTestIds.panelBanner)).toHaveText(
    'A copyright risk is open on this source.',
  );
  await expect(page.getByTestId(refsTestIds.verdictStatus)).toHaveText('Needs check');
});

test('SourcePanel_WhenUseTypeChanged_ShouldPatchTheUse', async ({ mount, page }) => {
  const useId = 'bbbbbbbb-0000-4000-8000-000000000001';
  let body: unknown;
  await page.route(`**/api/reference-uses/${useId}`, (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({
      json: stubDetail(needsCheck, {
        uses: [{ ...stubDetail(needsCheck).uses[0], kind: 'paraphrase' }],
      }),
    });
  });
  await open(mount, page, needsCheck.id);

  await page.getByTestId(refsTestIds.useKind(useId)).selectOption('paraphrase');

  await expect(page.getByTestId(refsTestIds.useKind(useId))).toHaveValue('paraphrase');
  expect(body).toEqual({ kind: 'paraphrase' });
});

test('SourcePanel_WhenAFieldIsEdited_ShouldSaveTheWholeFormOnBlur', async ({ mount, page }) => {
  let body: { details: { publisher: string; title: string } } | undefined;
  await page.route(`**/api/references/${needsCheck.id}`, (route) => {
    if (route.request().method() !== 'PATCH') return route.fallback();
    body = route.request().postDataJSON();
    return route.fulfill({
      json: stubDetail(needsCheck, {
        details: { ...stubDetail(needsCheck).details, publisher: 'Fresh Press' },
      }),
    });
  });
  await open(mount, page, needsCheck.id);

  const field = page.getByTestId(refsTestIds.field('publisher'));
  await field.fill('Fresh Press');
  await field.blur();

  await expect(field).toHaveValue('Fresh Press');
  expect(body?.details.publisher).toBe('Fresh Press');
  expect(body?.details.title).toBe(needsCheck.title);
});

test('SourcePanel_WhenAddressRedirects_ShouldOfferToUpdateIt', async ({ mount, page }) => {
  const redirecting = stubDetail(needsCheck, {
    details: {
      ...stubDetail(needsCheck).details,
      url: 'https://old.test/',
      identifiers: [{ field: 'url', status: 'redirect', resolvedUrl: 'https://new.test/' }],
    },
  });
  let body: unknown;
  await page.route(`**/api/references/${needsCheck.id}/recheck/accept`, (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({
      json: stubDetail(needsCheck, {
        details: {
          ...redirecting.details,
          url: 'https://new.test/',
          identifiers: [{ field: 'url', status: 'ok', resolvedUrl: null }],
        },
      }),
    });
  });
  await open(mount, page, needsCheck.id, async () => {
    await page.route(`**/api/references/${needsCheck.id}`, (route) =>
      route.request().method() === 'GET' ? route.fulfill({ json: redirecting }) : route.fallback(),
    );
  });
  await expect(page.getByTestId(refsTestIds.verification('url'))).toHaveText('Redirects');

  await page.getByTestId(refsTestIds.updateAddress('url')).click();

  await expect(page.getByTestId(refsTestIds.verification('url'))).toHaveText('Verified');
  expect(body).toEqual({ redirectTo: 'https://new.test/' });
});

test('SourcePanel_WhenRemoveConfirmed_ShouldArchiveAndClosePanel', async ({ mount, page }) => {
  await page.route(`**/api/references/${risk.id}/archive`, (route) =>
    route.fulfill({
      json: { scenes: [{ sceneId: 'x', chapterNumber: 4, sceneNumber: 1, uses: 1 }] },
    }),
  );
  await open(mount, page, risk.id);

  await page.getByTestId(refsTestIds.remove).click();
  await expect(page.getByTestId(refsTestIds.removeConfirm)).toBeVisible();
  await page.getByTestId(refsTestIds.removeConfirm).click();

  await expect(page.getByTestId(refsTestIds.panel)).toHaveCount(0);
  await expect(page.getByTestId('editing-toast')).toContainText('plain text');
});

test('SourcePanel_WhenRemoveCancelled_ShouldKeepTheSource', async ({ mount, page }) => {
  await open(mount, page, risk.id);

  await page.getByTestId(refsTestIds.remove).click();
  await page.getByTestId(refsTestIds.removeCancel).click();

  await expect(page.getByTestId(refsTestIds.remove)).toBeVisible();
  await expect(page.getByTestId(refsTestIds.panelTitle)).toHaveText(risk.title);
});

test('SourcePanel_WhenFairUseVerdict_ShouldAskForAReasonThenPost', async ({ mount, page }) => {
  const fairUse = stubDetail(needsCheck, {
    verdict: {
      ...stubDetail(needsCheck).verdict,
      reason: 'fair_use_undecided',
      action: 'accept_fair_use',
    },
  });
  let body: unknown;
  await page.route(`**/api/references/${needsCheck.id}/fair-use`, (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({ json: stubDetail(needsCheck) });
  });
  await open(mount, page, needsCheck.id, async () => {
    await page.route(`**/api/references/${needsCheck.id}`, (route) =>
      route.request().method() === 'GET' ? route.fulfill({ json: fairUse }) : route.fallback(),
    );
  });

  await page.getByTestId(refsTestIds.verdictAction).click();
  await expect(page.getByTestId(refsTestIds.fairUseAccept)).toBeDisabled();
  await page.getByTestId(refsTestIds.fairUseReason).fill('Seven words, quoted to discuss.');
  await page.getByTestId(refsTestIds.fairUseAccept).click();

  await expect(page.getByTestId(refsTestIds.verdictAction)).toHaveText('Mark as cleared');
  expect(body).toEqual({ reason: 'Seven words, quoted to discuss.' });
});

test('SourcePanel_WhenMergedIntoAnotherSource_ShouldPostTheTargetAndClosePanel', async ({
  mount,
  page,
}) => {
  const target = stubSources[0];
  let posted: unknown = null;
  await page.route(`**/api/references/${needsCheck.id}/merge`, (route) => {
    posted = route.request().postDataJSON();
    return route.fulfill({ json: stubDetail(target) });
  });
  await open(mount, page, needsCheck.id);

  await page.getByTestId(refsTestIds.panelMenu).click();
  await page.getByTestId(refsTestIds.panelMenuMerge).click();
  await expect(page.getByTestId(refsTestIds.mergeConfirm)).toBeDisabled();
  await expect(page.getByTestId(refsTestIds.mergeSummary)).toHaveText(
    "1 source's details and 1 use move; the duplicate is archived.",
  );
  const choices = page.getByTestId(refsTestIds.mergeTarget).locator('option');
  await expect(choices).not.toContainText([needsCheck.title]);
  await page.getByTestId(refsTestIds.mergeTarget).selectOption(target.id);
  await page.getByTestId(refsTestIds.mergeConfirm).click();

  await expect(page.getByTestId(refsTestIds.panel)).toHaveCount(0);
  expect(posted).toEqual({ intoId: target.id });
});

test('SourcePanel_WhenTheMergeIsBlocked_ShouldShowTheReasonAndKeepTheDialog', async ({
  mount,
  page,
}) => {
  await page.route(`**/api/references/${needsCheck.id}/merge`, (route) =>
    route.fulfill({
      status: 422,
      json: { error: 'Both have a permission request. Withdraw one first.', code: 'blocked' },
    }),
  );
  await open(mount, page, needsCheck.id);

  await page.getByTestId(refsTestIds.panelMenu).click();
  await page.getByTestId(refsTestIds.panelMenuMerge).click();
  await page.getByTestId(refsTestIds.mergeTarget).selectOption(stubSources[0].id);
  await page.getByTestId(refsTestIds.mergeConfirm).click();

  await expect(page.getByTestId(refsTestIds.panelBanner)).toContainText('Withdraw one first');
  await expect(page.getByTestId(refsTestIds.mergeDialog)).toBeVisible();
});
