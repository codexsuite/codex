import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../../AppRoutes';
import type { Permission, SourceDetail } from '@codex/core/api/referenceSources';
import { stubDetail, stubReferencesApi, stubSources } from '../../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../../test-support/shellStubs';
import { refsTestIds } from '../testIds';

const book = allBooks(library)[0];
const risk = stubSources[2];
const requestId = 'cccccccc-0000-4000-8000-000000000001';

const permission = (extra: Partial<Permission>): Permission => ({
  id: requestId,
  status: 'draft',
  to: '',
  letter: 'Dear Rights Holder,\n\n342 words.',
  sentLabel: null,
  remindOn: null,
  remindLabel: null,
  remindDue: false,
  respondedLabel: null,
  creditLine: null,
  terms: null,
  note: null,
  attachment: null,
  followUp: null,
  followedUpLabel: null,
  feeAmount: null,
  ...extra,
});

interface Call {
  method: string;
  url: string;
  body: string | null;
}

// A tiny stand-in for the server: `current` is what GET answers, and each write replaces it with the next reply.
async function open(
  mount: (node: React.JSX.Element, options: object) => Promise<unknown>,
  page: Page,
  start: SourceDetail,
  replies: Record<string, SourceDetail | { status: number; error: string }>,
) {
  const calls: Call[] = [];
  let current = start;
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.route(`**/api/references/${start.id}`, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ json: current }) : route.fallback(),
  );
  const reply = async (route: import('playwright').Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    calls.push({ method: request.method(), url: path, body: request.postData() });
    const next = replies[`${request.method()} ${path.split('/').slice(-1)[0]}`];
    if (next === undefined)
      return route.fulfill({ status: 500, json: { error: 'no reply', code: 'internal_error' } });
    if ('status' in next) {
      return route.fulfill({ status: next.status, json: { error: next.error, code: 'conflict' } });
    }
    current = next;
    return route.fulfill({ json: next });
  };
  await page.route(`**/api/references/${start.id}/permission`, reply);
  await page.route('**/api/permission-requests/**', reply);
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${book.id}/references#${start.id}` },
  });
  return calls;
}

const riskDetail = (extra: Partial<SourceDetail> = {}) =>
  stubDetail(risk, {
    verdict: {
      status: 'risk',
      reason: 'quote_over_limit',
      action: 'permission',
      signedOffBy: null,
      signedOffLabel: null,
    },
    ...extra,
  });

test('PermissionTracker_WhenARiskHasNoRequest_ShouldOfferDraftRequestAndShowTheLetter', async ({
  mount,
  page,
}) => {
  const drafted = riskDetail({ permission: permission({}) });
  const calls = await open(mount, page, riskDetail(), { 'POST permission': drafted });

  await page.getByTestId(refsTestIds.permDraft).click();

  await expect(page.getByTestId(refsTestIds.permLetter)).toHaveValue(/342 words/);
  await expect(page.getByTestId(refsTestIds.permission)).toHaveAttribute('data-status', 'draft');
  expect(calls.map((call) => call.url)).toEqual([`/api/references/${risk.id}/permission`]);
});

test('PermissionTracker_WhenTheDraftIsEdited_ShouldSaveOnBlurAndSendWithMarkSent', async ({
  mount,
  page,
}) => {
  const start = riskDetail({ permission: permission({}) });
  const edited = riskDetail({ permission: permission({ letter: 'Dear Ms R,\n\nMay I?' }) });
  const sent = riskDetail({
    permission: permission({
      status: 'sent',
      letter: 'Dear Ms R,\n\nMay I?',
      sentLabel: 'Asked 19 Oct',
      remindOn: '2026-11-02',
      remindLabel: 'Reminder 2 Nov',
    }),
  });
  const calls = await open(mount, page, start, { 'PATCH x': edited, 'POST sent': sent });
  await page.route(`**/api/permission-requests/${requestId}`, async (route) => {
    calls.push({
      method: 'PATCH',
      url: new URL(route.request().url()).pathname,
      body: route.request().postData(),
    });
    await route.fulfill({ json: edited });
  });

  await page.getByTestId(refsTestIds.permLetter).fill('Dear Ms R,\n\nMay I?');
  await page.getByTestId(refsTestIds.permSend).click();

  await expect(page.getByTestId(refsTestIds.permSentLabel)).toHaveText('Asked 19 Oct · no reply');
  await expect(page.getByTestId(refsTestIds.permRemindLabel)).toHaveText('Reminder 2 Nov');
  expect(calls.some((call) => call.method === 'PATCH' && call.body?.includes('May I?'))).toBe(true);
  expect(calls.at(-1)?.url).toBe(`/api/permission-requests/${requestId}/sent`);
});

test('PermissionTracker_WhenSentAndGranted_ShouldUploadTheLetterAndShowTheCreditLine', async ({
  mount,
  page,
}) => {
  const sent = riskDetail({ permission: permission({ status: 'sent', sentLabel: 'Asked 3 Oct' }) });
  const granted = riskDetail({
    verdict: {
      status: 'needs_check',
      reason: 'ready_to_sign_off',
      action: 'sign_off',
      signedOffBy: null,
      signedOffLabel: null,
    },
    permission: permission({
      status: 'granted',
      sentLabel: 'Asked 3 Oct',
      respondedLabel: 'Granted 19 Oct',
      creditLine: 'Used by permission.',
      attachment: { fileName: 'letter.pdf', url: `/api/permission-requests/${requestId}/letter` },
    }),
  });
  const calls = await open(mount, page, sent, { 'POST granted': granted });

  await page.getByTestId(refsTestIds.permGranted).click();
  await expect(page.getByTestId(refsTestIds.permGrantSubmit)).toBeDisabled();
  await page.getByTestId(refsTestIds.permFile).setInputFiles({
    name: 'letter.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  });
  await page.getByTestId(refsTestIds.permCreditInput).fill('Used by permission.');
  await page.getByTestId(refsTestIds.permGrantSubmit).click();

  await expect(page.getByTestId(refsTestIds.permCredit)).toHaveText(
    'Credit line: Used by permission.',
  );
  await expect(page.getByTestId(refsTestIds.permAttachment)).toHaveText('letter.pdf');
  expect(calls[0].body).toContain('name="letter"; filename="letter.pdf"');
  expect(calls[0].body).toContain('Used by permission.');
});

test('PermissionTracker_WhenTheServerRefusesATransition_ShouldShowTheErrorAndKeepTheState', async ({
  mount,
  page,
}) => {
  const sent = riskDetail({ permission: permission({ status: 'sent', sentLabel: 'Asked 3 Oct' }) });
  await open(mount, page, sent, {
    'POST withdraw': {
      status: 409,
      error: 'Only a draft or a request waiting for an answer can be withdrawn.',
    },
  });

  await page.getByTestId(refsTestIds.permWithdraw).click();

  await expect(page.getByTestId(refsTestIds.panelBanner)).toContainText('can be withdrawn');
  await expect(page.getByTestId(refsTestIds.permission)).toHaveAttribute('data-status', 'sent');
});

test('PermissionTracker_WhenRefused_ShouldShowTheNoteAndOfferANewRequest', async ({
  mount,
  page,
}) => {
  const sent = riskDetail({ permission: permission({ status: 'sent', sentLabel: 'Asked 3 Oct' }) });
  const refused = riskDetail({
    permission: permission({
      status: 'refused',
      sentLabel: 'Asked 3 Oct',
      respondedLabel: 'Refused 19 Oct',
      note: 'Not licensed.',
    }),
  });
  const calls = await open(mount, page, sent, { 'POST refused': refused });

  await page.getByTestId(refsTestIds.permRefused).click();
  await page.getByTestId(refsTestIds.permRefuseNote).fill('Not licensed.');
  await page.getByTestId(refsTestIds.permRefuseSubmit).click();

  await expect(page.getByTestId(refsTestIds.permNote)).toHaveText('Not licensed.');
  await expect(page.getByTestId(refsTestIds.permDraft)).toHaveText('Draft a new request');
  expect(calls[0].body).toContain('Not licensed.');
});

test('VerdictBlock_WhenAQuoteIsOverTheLimit_ShouldOfferToAskOrParaphrase', async ({
  mount,
  page,
}) => {
  const drafted = riskDetail({ bookId: book.id, permission: permission({}) });
  const calls = await open(mount, page, riskDetail({ bookId: book.id }), {
    'POST permission': drafted,
  });

  await expect(page.getByTestId(refsTestIds.verdictNote)).toHaveText(
    'Over the limit for quoting without permission. Shorten, paraphrase, or ask.',
  );
  await expect(page.getByTestId(refsTestIds.verdictParaphrase)).toHaveAttribute(
    'href',
    `/books/${book.id}/write#scene=4.1&ref=${risk.id}&paraphrase=1`,
  );
  await expect(page.getByTestId(refsTestIds.permRemoveUses)).toHaveAttribute(
    'href',
    `/books/${book.id}/write#scene=4.1&ref=${risk.id}`,
  );
  await page.getByTestId(refsTestIds.verdictAsk).click();

  await expect(page.getByTestId(refsTestIds.permission)).toHaveAttribute('data-status', 'draft');
  await expect(page.getByTestId(refsTestIds.verdictAsk)).toHaveCount(0);
  expect(calls.map((call) => call.url)).toEqual([`/api/references/${risk.id}/permission`]);
});

test('PermissionTracker_WhenSentAndOverdue_ShouldSnoozeCopyAndMarkFollowedUp', async ({
  mount,
  page,
}) => {
  const overdue = permission({
    status: 'sent',
    sentLabel: 'Asked 3 Oct',
    remindLabel: 'Reminder due',
    remindDue: true,
    followUp: 'Hello,\n\nI am following up on my request of 3 Oct.',
  });
  const snoozed = permission({ ...overdue, remindLabel: 'Reminder 29 Oct', remindDue: false });
  const followed = permission({ ...snoozed, followedUpLabel: 'Followed up 22 Oct' });
  const calls = await open(mount, page, riskDetail({ permission: overdue }), {
    [`PATCH ${requestId}`]: riskDetail({ permission: snoozed }),
    'POST followed-up': riskDetail({ permission: followed }),
  });
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);

  await expect(page.getByTestId(refsTestIds.permRemindLabel)).toHaveText('Reminder due');
  await page.getByTestId(refsTestIds.permRemindIn(7)).click();
  await expect(page.getByTestId(refsTestIds.permRemindLabel)).toHaveText('Reminder 29 Oct');
  expect(JSON.parse(calls[0].body ?? '{}')).toMatchObject({ remindInDays: 7 });

  await page.getByTestId(refsTestIds.permCopyFollowUp).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(
    'following up on my request of 3 Oct',
  );

  await page.getByTestId(refsTestIds.permFollowedUp).click();
  await expect(page.getByTestId(refsTestIds.permFollowedUpLabel)).toHaveText('Followed up 22 Oct');
});

test('PermissionTracker_WhenGrantedWithALetter_ShouldRemoveItAndAskForAnother', async ({
  mount,
  page,
}) => {
  const withLetter = permission({
    status: 'granted',
    respondedLabel: 'Granted 19 Oct',
    attachment: { fileName: 'grant.pdf', url: `/api/permission-requests/${requestId}/letter` },
  });
  const without = permission({ ...withLetter, attachment: null });
  const replaced = permission({
    ...withLetter,
    attachment: { fileName: 'grant-v2.pdf', url: `/api/permission-requests/${requestId}/letter` },
  });
  const calls = await open(mount, page, riskDetail({ permission: withLetter }), {
    'DELETE letter': riskDetail({ permission: without }),
    'POST letter': riskDetail({ permission: replaced }),
  });

  await page.getByTestId(refsTestIds.permAttachmentRemove).click();
  await page.getByTestId(refsTestIds.permAttachmentConfirm).click();
  await expect(page.getByTestId(refsTestIds.permAttachment)).toHaveCount(0);
  await page.getByTestId(refsTestIds.permAttachInput).setInputFiles({
    name: 'grant-v2.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  });

  await expect(page.getByTestId(refsTestIds.permAttachment)).toHaveText('grant-v2.pdf');
  expect(calls.map((call) => call.method)).toEqual(['DELETE', 'POST']);
});

test('GrantForm_WhenTheFeeIsNegativeOrCancelled_ShouldShowAnErrorAndPostNothing', async ({
  mount,
  page,
}) => {
  const sent = riskDetail({ permission: permission({ status: 'sent', sentLabel: 'Asked 3 Oct' }) });
  const calls = await open(mount, page, sent, {});

  await page.getByTestId(refsTestIds.permGranted).click();
  await page.getByTestId(refsTestIds.permFile).setInputFiles({
    name: 'letter.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  });
  await page.getByTestId(refsTestIds.permFeeInput).fill('-5');
  await page.getByTestId(refsTestIds.permGrantSubmit).click();
  await expect(page.getByTestId(refsTestIds.permFormError)).toContainText('zero or more');

  await page.getByTestId(refsTestIds.permGrantCancel).click();
  await expect(page.getByTestId(refsTestIds.permGrantForm)).toHaveCount(0);
  await expect(page.getByTestId(refsTestIds.permission)).toHaveAttribute('data-status', 'sent');
  expect(calls).toEqual([]);
});

test('VerdictBlock_WhenPermissionWasRefused_ShouldLinkToCutThePassageInWrite', async ({
  mount,
  page,
}) => {
  const refused = riskDetail({
    bookId: book.id,
    verdict: {
      status: 'risk',
      reason: 'permission_refused',
      action: 'permission',
      signedOffBy: null,
      signedOffLabel: null,
    },
    permission: permission({ status: 'refused', respondedLabel: 'Refused 19 Oct' }),
  });
  await open(mount, page, refused, {});

  await expect(page.getByTestId(refsTestIds.verdictCut)).toHaveAttribute(
    'href',
    `/books/${book.id}/write#scene=4.1&ref=${risk.id}`,
  );
});
