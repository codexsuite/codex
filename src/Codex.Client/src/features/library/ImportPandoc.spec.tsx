import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { importTestIds } from './importTestIds';
import { libraryTestIds } from './testIds';

// The made-up Players Guide folder the pandoc import's own tests use: chapters, a defaults .yaml and nine images.
const folder = fileURLToPath(
  new URL('../../../../Codex.TestSupport/Fixtures/import/players-guide-campaign', import.meta.url),
);
const used = 'players-guide-campaign/images/Scenes/Harrowmere/TOC.png';
const unused = 'players-guide-campaign/images/Scenes/Harbourfall/Chapter 02 Humans.png';

test('Import_WhenPandocFolderPicked_ShouldPreviewTheTextAndSendOnlyTheListedImages', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  let previewBody = '';
  let runBody = '';
  await page.route('**/api/imports/preview', (r) => {
    previewBody = r.request().postData() ?? '';
    return r.fulfill({
      json: {
        source: 'pandoc',
        bookTitle: 'Players Guide to The Realm of Velmara',
        fileCount: 15,
        words: 2400,
        creates: [{ item: 'Images', count: 1, destination: 'Assets' }],
        skipped: [{ path: unused, reason: 'An image the chapters do not use' }],
        images: [used],
      },
    });
  });
  await page.route('**/api/imports', (r) => {
    runBody = r.request().postData() ?? '';
    return r.fulfill({ status: 400, json: { error: 'Stopped here.', code: 'validation' } });
  });
  await page.getByTestId(libraryTestIds.import).click();
  await page.getByTestId(libraryTestIds.importSource('pandoc')).click();
  await page.getByTestId(libraryTestIds.importFile).setInputFiles(folder);
  await page.getByTestId(libraryTestIds.importNext).click();
  await expect(page.getByTestId(importTestIds.previewRow('Images'))).toContainText('Assets');

  await page.getByTestId(importTestIds.run).click();

  await expect(page.getByTestId(importTestIds.runError)).toHaveText('Stopped here.');
  expect(previewBody).toContain('filename="players-guide-campaign/PlayersGuide.yaml"');
  expect(previewBody).toContain('filename="players-guide-campaign/Chapter 3.md"');
  expect(previewBody).not.toContain('filename="players-guide-campaign/images/');
  expect(previewBody).toContain(used);
  expect(runBody).toContain(`filename="${used}"`);
  expect(runBody).not.toContain(`filename="${unused}"`);
  expect(runBody).toContain(unused);
});
