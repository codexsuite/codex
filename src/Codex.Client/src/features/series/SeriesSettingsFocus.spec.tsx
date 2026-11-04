import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { stubShellApi } from '../../test-support/shellStubs';
import { seriesTestIds } from './testIds';

const series = library.series[0];

// Series settings (#16 review section D, W4.8): it takes focus when it opens, keeps Tab inside it, and gives focus back
// to the settings button however it closes. Esc is pinned in SeriesSettings.spec.tsx.

async function openSettingsByKeyboard(
  page: Parameters<typeof stubShellApi>[0],
  mount: (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>,
) {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${series.id}` } });
  await page.getByTestId(seriesTestIds.settingsButton).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(seriesTestIds.dialog)).toBeVisible();
}

test('SeriesSettings_WhenOpen_ShouldTakeFocusAndKeepTabInsideIt', async ({ mount, page }) => {
  await openSettingsByKeyboard(page, mount);
  const dialog = page.getByTestId(seriesTestIds.dialog);

  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
});

for (const [how, id] of [
  ['CloseButton', seriesTestIds.close],
  ['CancelButton', seriesTestIds.cancel],
] as const) {
  test(`SeriesSettings_When${how}Pressed_ShouldCloseAndReturnFocusToOpener`, async ({
    mount,
    page,
  }) => {
    await openSettingsByKeyboard(page, mount);

    await page.getByTestId(id).press('Enter');

    await expect(page.getByTestId(seriesTestIds.dialog)).toBeHidden();
    await expect(page.getByTestId(seriesTestIds.settingsButton)).toBeFocused();
  });
}
