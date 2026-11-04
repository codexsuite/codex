import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { editingTestIds } from '../editing/testIds';
import { ShellToastHarness } from '../test-support/ShellToastHarness';
import { shellToastHarnessIds } from '../test-support/shellToastHarnessIds';
import { stubShellApi } from '../test-support/shellStubs';
import { testIds } from '../testIds';

const hymns = library.series[0].books[1].id;

for (const route of ['/', `/books/${hymns}/plan`]) {
  test(`ShowToast_WhenCalledFromAPage_ShouldRenderInTheShell ${route}`, async ({ mount, page }) => {
    await stubShellApi(page, library);
    await mount(<ShellToastHarness />, { hooksConfig: { route } });
    await expect(page.getByTestId(testIds.shell.appShell)).toBeVisible();

    await page.getByTestId(shellToastHarnessIds.show).click();

    const shell = page.getByTestId(testIds.shell.appShell);
    await expect(shell.getByTestId(editingTestIds.toastRegion)).toHaveCount(1);
    await expect(shell.getByTestId(editingTestIds.toast)).toHaveText(/Archived 1 scene/);
    await shell.getByTestId(editingTestIds.toastUndo).click();
    await expect(page.getByTestId(shellToastHarnessIds.undos)).toHaveText('1');
    await expect(shell.getByTestId(editingTestIds.toast)).toHaveCount(0);
  });
}
