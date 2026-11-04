import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/experimental-ct-react';
import { ProseHarness } from '../test-support/ProseHarness';
import { proseTestIds } from './testIds';

// The Players Guide after the LaTeX import mapping (src/Codex.TestSupport/Fixtures/players-guide/README.md).
const directory = join(process.cwd(), '../Codex.TestSupport/Fixtures/players-guide/mapped');
const chapters = readdirSync(directory).filter((f) => f.endsWith('.md'));

test('Chapters_ShouldBeAllTen', () => {
  expect(chapters).toHaveLength(10);
});

for (const name of chapters) {
  const markdown = readFileSync(join(directory, name), 'utf8');
  test(`MarkdownSurface_WhenToggledRichSourceRichRepeatedly_ShouldKeepTheChapterAndEmitNothing ${name}`, async ({
    mount,
    page,
  }) => {
    await mount(<ProseHarness initial={markdown} surface="scene-prose" />);
    await expect(page.getByTestId(proseTestIds.editor)).toBeVisible();

    for (let round = 0; round < 3; round += 1) {
      await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
      await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toBeVisible();
      await page.getByTestId(proseTestIds.modeRich('scene-prose')).click();
      await expect(page.getByTestId(proseTestIds.editor)).toBeVisible();
    }

    await expect(page.getByTestId('harness-changes')).toHaveText('');
    expect(await page.getByTestId('harness-markdown').textContent()).toBe(markdown);
  });
}
