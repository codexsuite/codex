import { expect, test } from '@playwright/experimental-ct-react';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import prompts from '../../../../Codex.TestSupport/Fixtures/prompts.json' with { type: 'json' };
import { aiModelChoices } from '../ai/aiFixtures';
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);

const beat = {
  id: '0199a000-0000-7000-8000-0000000000f1',
  sceneId: first.id,
  body: 'Guyon crosses the yard and meets the guide.',
  targetWordCount: 400,
  isHidden: false,
  contextEntryIds: [] as string[],
};

test('WritePage_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [beat] } }),
  );
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route(`**/api/books/${book.id}/markers`, (route) => route.fulfill({ json: [] }));
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({ json: { bookId: book.id, drafts: [] } }),
  );
  await page.route('**/api/prompts', (route) => route.fulfill({ json: prompts }));
  await page.route('**/api/ai/models', (route) => route.fulfill({ json: aiModelChoices }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.manuscript)).toBeVisible();
  await expect(page.getByText('Guyon crossed the yard.')).toBeVisible();
  await page.evaluate(() => document.fonts.ready);

  await expect(page).toHaveScreenshot('write-default-light.png');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page).toHaveScreenshot('write-default-dark.png');
});
