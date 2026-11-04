import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { AiTemplates } from '@codex/core/api/ai';
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { aiTestIds as ids } from './testIds';

const mine = '0199c0de-0000-7000-8000-0000000a1001';

const templates: AiTemplates = {
  templates: [
    {
      id: 'joels-voice',
      name: "Write in Joel's voice",
      description: 'Draft the beat in the book voice.',
      body: 'Keep the voice.',
      builtIn: true,
    },
    {
      id: mine,
      name: 'Mine',
      description: '',
      body: 'Write {{beat}}',
      builtIn: false,
    },
  ],
  variables: ['beat', 'context', 'style_guide', 'previous_prose', 'length'],
};

// Settings → AI with no provider connected: the template section does not depend on one.
async function stub(page: Page) {
  const calls: { method: string; url: string; body: unknown }[] = [];
  await stubShellApi(page, library);
  await page.route('**/api/ai/integrations', (route) =>
    route.fulfill({ json: { integrations: [], localEndpointsAllowed: false } }),
  );
  await page.route('**/api/ai/templates', (route) => route.fulfill({ json: templates }));
  await page.route('**/api/ai/templates/**', (route) => {
    const request = route.request();
    calls.push({ method: request.method(), url: request.url(), body: request.postDataJSON() });
    if (request.url().endsWith('/duplicate')) {
      return route.fulfill({
        status: 409,
        json: { error: 'You already have a template with that name.', code: 'conflict' },
      });
    }
    return route.fulfill({ status: 204 });
  });
  return calls;
}

const open = (mount: Parameters<Parameters<typeof test>[2]>[0]['mount']) =>
  mount(<AppRoutes />, { hooksConfig: { route: '/settings/ai' } });

test('Templates_WhenBuiltIn_ShouldOfferDuplicateOnlyAndYoursCanBeEdited', async ({
  mount,
  page,
}) => {
  await stub(page);
  const component = await open(mount);

  await expect(component.getByTestId(ids.templateBuiltIn('joels-voice'))).toHaveText('Built in');
  await expect(component.getByTestId(ids.templateEdit('joels-voice'))).toHaveCount(0);
  await expect(component.getByTestId(ids.templateDuplicate('joels-voice'))).toBeVisible();
  await expect(component.getByTestId(ids.templateBuiltIn(mine))).toHaveCount(0);
  await expect(component.getByTestId(ids.templateEdit(mine))).toBeVisible();
});

test('Duplicate_WhenTheNameIsTaken_ShouldShowTheErrorInline', async ({ mount, page }) => {
  await stub(page);
  const component = await open(mount);

  await component.getByTestId(ids.templateDuplicate('joels-voice')).click();
  await component.getByTestId(ids.duplicateName).fill('Mine');
  await component.getByTestId(ids.duplicateSave).click();

  await expect(component.getByTestId(ids.duplicateError)).toHaveText(
    'You already have a template with that name.',
  );
});

test('Editor_WhenTheBodyIsEmptyOrLacksTheBeat_ShouldDisableSaveOrWarn', async ({ mount, page }) => {
  await stub(page);
  const component = await open(mount);

  await component.getByTestId(ids.templateEdit(mine)).click();
  await expect(component.getByTestId(ids.editorTokens)).toContainText('About 4 tokens');
  await component.getByTestId(ids.editorBody).fill('No placeholder here');
  await expect(component.getByTestId(ids.editorWarning)).toHaveText(
    'The model will not see the beat',
  );
  await expect(component.getByTestId(ids.editorSave)).toBeEnabled();
  await component.getByTestId(ids.editorBody).fill('   ');

  await expect(component.getByTestId(ids.editorError)).toHaveText('The prompt cannot be empty');
  await expect(component.getByTestId(ids.editorSave)).toBeDisabled();
});
