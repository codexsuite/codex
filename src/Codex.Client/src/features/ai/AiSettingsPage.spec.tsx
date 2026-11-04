import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { AiIntegration, AiSettings } from '@codex/core/api/ai';
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { aiTestIds } from './testIds';

const gateway: AiIntegration = {
  id: '0199a000-0000-7000-8000-0000000000c1',
  kind: 'openai_compatible',
  label: 'Gateway',
  baseUrl: 'https://gateway.test/v1',
  keyHint: 'sk-…abcd',
  defaultModel: 'gpt-5',
  models: ['gpt-5', 'gpt-5-mini'],
  isDefault: true,
  createdLabel: '1 Oct',
  lastUsedLabel: '2h ago',
  lastError: 'The key was refused (401).',
};

const openRouter: AiIntegration = {
  ...gateway,
  id: '0199a000-0000-7000-8000-0000000000c2',
  kind: 'openrouter',
  label: 'Router',
  baseUrl: null,
  keyHint: 'sk-…wxyz',
  defaultModel: null,
  models: [],
  isDefault: false,
  lastUsedLabel: null,
  lastError: null,
};

async function stub(page: Page, settings: AiSettings) {
  await stubShellApi(page, library);
  await page.route('**/api/ai/integrations', (route) => route.fulfill({ json: settings }));
}

test('AiSettings_WhenNothingConnected_ShouldShowTheEmptyState', async ({ mount, page }) => {
  await stub(page, { integrations: [], localEndpointsAllowed: false });
  await mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

  await expect(page.getByTestId(aiTestIds.empty)).toHaveText(
    'No AI provider connected. Connect one to draft prose in Write.',
  );
});

test('AiSettings_WhenConnected_ShouldListEachIntegrationWithoutItsKey', async ({ mount, page }) => {
  await stub(page, { integrations: [gateway, openRouter], localEndpointsAllowed: false });
  await mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

  const row = page.getByTestId(aiTestIds.row(gateway.id));
  await expect(row).toContainText('OpenAI-compatible');
  await expect(row).toContainText('https://gateway.test/v1');
  await expect(page.getByTestId(aiTestIds.rowKeyHint(gateway.id))).toHaveText('sk-…abcd');
  await expect(page.getByTestId(aiTestIds.rowModel(gateway.id))).toHaveText('gpt-5');
  await expect(page.getByTestId(aiTestIds.rowDefaultBadge(gateway.id))).toBeVisible();
  await expect(page.getByTestId(aiTestIds.rowLastError(gateway.id))).toContainText('refused');
  await expect(page.getByTestId(aiTestIds.rowLastUsed(openRouter.id))).toHaveText('Never');
  await expect(page.getByTestId(aiTestIds.rowDefaultBadge(openRouter.id))).toHaveCount(0);
  await expect(page.getByTestId(aiTestIds.rowMakeDefault(openRouter.id))).toBeVisible();
});

test('Connect_WhenTestedThenSaved_ShouldPostTheKeyOnceAndClearIt', async ({ mount, page }) => {
  await stub(page, { integrations: [], localEndpointsAllowed: false });
  const created: unknown[] = [];
  await page.route('**/api/ai/integrations/test', (route) =>
    route.fulfill({ json: { ok: true, models: ['gpt-5', 'gpt-5-mini'], error: null } }),
  );
  await page.route('**/api/ai/integrations', async (route) => {
    if (route.request().method() === 'POST') {
      created.push(route.request().postDataJSON() as unknown);
      await route.fulfill({ json: { ...gateway, lastError: null } });
    } else {
      await route.fulfill({ json: { integrations: [], localEndpointsAllowed: false } });
    }
  });
  await mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

  await page.getByTestId(aiTestIds.connect).click();
  await expect(page.getByTestId(aiTestIds.dialogBaseUrlHint)).toHaveText('https:// only');
  await page.getByTestId(aiTestIds.dialogLabel).fill('Gateway');
  await page.getByTestId(aiTestIds.dialogBaseUrl).fill('https://gateway.test/v1');
  await page.getByTestId(aiTestIds.dialogApiKey).fill('sk-secret-abcd');
  await page.getByTestId(aiTestIds.dialogTest).click();
  await expect(page.getByTestId(aiTestIds.dialogTestResult)).toHaveText('Connected: 2 models');
  await page.getByTestId(aiTestIds.dialogModel).selectOption('gpt-5-mini');
  await page.getByTestId(aiTestIds.dialogSave).click();

  await expect(page.getByTestId(aiTestIds.dialog)).toHaveCount(0);
  expect(created).toEqual([
    {
      kind: 'openai_compatible',
      label: 'Gateway',
      baseUrl: 'https://gateway.test/v1',
      apiKey: 'sk-secret-abcd',
      defaultModel: 'gpt-5-mini',
    },
  ]);
});

test('Connect_WhenTheServerRefusesTheBaseUrl_ShouldShowItsMessage', async ({ mount, page }) => {
  await stub(page, { integrations: [], localEndpointsAllowed: true });
  await page.route('**/api/ai/integrations/test', (route) =>
    route.fulfill({
      status: 400,
      json: { error: 'That address is not allowed.', code: 'validation' },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

  await page.getByTestId(aiTestIds.connect).click();
  await expect(page.getByTestId(aiTestIds.dialogBaseUrlHint)).toHaveCount(0);
  await page.getByTestId(aiTestIds.dialogBaseUrl).fill('http://10.0.0.1/v1');
  await page.getByTestId(aiTestIds.dialogApiKey).fill('sk-x');
  await page.getByTestId(aiTestIds.dialogTest).click();

  await expect(page.getByTestId(aiTestIds.dialogError)).toHaveText('That address is not allowed.');
});

test('Row_WhenRemoveConfirmed_ShouldDeleteTheIntegration', async ({ mount, page }) => {
  await stub(page, { integrations: [gateway], localEndpointsAllowed: false });
  const deleted: string[] = [];
  await page.route(`**/api/ai/integrations/${gateway.id}`, async (route) => {
    deleted.push(route.request().method());
    await route.fulfill({ status: 204 });
  });
  await mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

  await page.getByTestId(aiTestIds.rowRemove(gateway.id)).click();
  await page.getByTestId(aiTestIds.rowRemoveConfirm(gateway.id)).click();

  await expect.poll(() => deleted).toEqual(['DELETE']);
});
