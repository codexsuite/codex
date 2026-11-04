import { expect, test } from '@playwright/experimental-ct-react';
import { ProseHarness } from '../test-support/ProseHarness';
import { proseTestIds } from './testIds';

const BEAT = '0199c0de-0000-7000-8000-000700000003';
const KENT = '0199c0de-0000-7000-8000-000b0000000b';
const MODE_KEY = (surface: string) => `codex.editor.mode.anonymous.${surface}`;

// Not canonical Markdown: the serializer would write one blank line between paragraphs and a final newline.
const NON_CANONICAL = `Opening line.\n\n\n\nSecond.`;

test('MarkdownSurface_WhenToggledBothWays_ShouldShowTheSameStoredTextAndEmitNothing', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={NON_CANONICAL} surface="scene-prose" />);
  await expect(page.getByTestId(proseTestIds.editor)).toContainText('Opening line.');

  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toBeVisible();
  await expect(page.getByTestId(proseTestIds.editor)).toHaveCount(0);
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toHaveText(
    'Opening line.Second.',
  );

  await page.getByTestId(proseTestIds.modeRich('scene-prose')).click();
  await expect(page.getByTestId(proseTestIds.editor)).toContainText('Opening line.');
  await expect(page.getByTestId('harness-markdown')).toHaveText(NON_CANONICAL);
  await expect(page.getByTestId('harness-changes')).toHaveText('');
});

test('MarkdownSurface_WhenTypedInSource_ShouldEmitFromSourceAndShowInRich', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial="Opening line." surface="scene-prose" />);
  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  const source = page.getByTestId(proseTestIds.sourceEditor('scene-prose'));
  await source.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' More.');
  await expect(page.getByTestId('harness-markdown')).toHaveText('Opening line. More.');
  expect(await page.getByTestId('harness-changes').textContent()).toMatch(/^source(,source)*$/);

  await page.getByTestId(proseTestIds.modeRich('scene-prose')).click();
  await expect(page.getByTestId(proseTestIds.editor)).toContainText('Opening line. More.');
  await expect(page.getByTestId('harness-markdown')).toHaveText('Opening line. More.');
});

test('MarkdownSurface_WhenControlSlashPressed_ShouldToggleInBothViews', async ({ mount, page }) => {
  await mount(<ProseHarness initial="Opening line." surface="scene-prose" />);
  await page.getByTestId(proseTestIds.editor).click();
  await page.keyboard.press('Control+/');
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toBeVisible();
  await page.getByTestId(proseTestIds.sourceEditor('scene-prose')).click();
  await page.keyboard.press('Control+/');
  await expect(page.getByTestId(proseTestIds.editor)).toBeVisible();
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toHaveCount(0);
  await expect(page.getByTestId('harness-changes')).toHaveText('');
});

test('MarkdownSurface_WhenRichEditedAfterNonCanonicalText_ShouldCanonicaliseOnlyThen', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={NON_CANONICAL} surface="scene-prose" />);
  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  await page.getByTestId(proseTestIds.modeRich('scene-prose')).click();
  await expect(page.getByTestId('harness-markdown')).toHaveText(NON_CANONICAL);
  await page.getByTestId(proseTestIds.editor).getByText('Opening line.').click();
  await page.keyboard.press('End');
  await page.keyboard.type('!');
  await expect(page.getByTestId('harness-markdown')).toHaveText('Opening line.!\n\nSecond.\n');
});

test('MarkdownSurface_WhenChoiceMade_ShouldRememberItPerSurface', async ({ mount, page }) => {
  await page.evaluate((key) => window.localStorage.setItem(key, 'source'), MODE_KEY('kb-entry'));
  await mount(<ProseHarness initial="Entry body." surface="kb-entry" />);
  await expect(page.getByTestId(proseTestIds.sourceEditor('kb-entry'))).toBeVisible();
  await page.getByTestId(proseTestIds.modeRich('kb-entry')).click();
  await expect(page.getByTestId(proseTestIds.editor)).toBeVisible();
  expect(await page.evaluate((key) => window.localStorage.getItem(key), MODE_KEY('kb-entry'))).toBe(
    'rich',
  );
  expect(
    await page.evaluate((key) => window.localStorage.getItem(key), MODE_KEY('scene-prose')),
  ).toBeNull();
});

test('MarkdownSurface_WhenAnotherSurfaceChoseSource_ShouldStayRich', async ({ mount, page }) => {
  await page.evaluate((key) => window.localStorage.setItem(key, 'source'), MODE_KEY('scene-prose'));
  await mount(<ProseHarness initial="Summary text." summary surface="scene-summary" />);
  await expect(page.getByTestId(proseTestIds.summaryEditor)).toBeVisible();
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-summary'))).toHaveCount(0);
});

test('MarkdownSurface_WhenOpaqueBlockInSource_ShouldBeEditable', async ({ mount, page }) => {
  const initial = `Opening line.\n\n::future{x="y"}\n`;
  await mount(<ProseHarness initial={initial} surface="scene-prose" />);
  await expect(page.getByTestId(proseTestIds.opaqueCard)).toBeVisible();

  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  await expect(page.getByTestId(proseTestIds.sourceEditor('scene-prose'))).toContainText(
    '::future{x="y"}',
  );
  await page.getByTestId(proseTestIds.sourceEditor('scene-prose')).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type('z');
  await expect(page.getByTestId('harness-markdown')).toHaveText(
    `Opening line.\n\n::future{x="y"}\nz`,
  );
});

test('MarkdownSurface_WhenMentionAndDirectiveInSource_ShouldKeepTextExact', async ({
  mount,
  page,
}) => {
  const initial = `See [Kent Beck](kb:${KENT}).\n\n::beat{id="${BEAT}"}\n\n\`\`\`{=latex}\n\\newpage\n\`\`\`\n`;
  await mount(<ProseHarness initial={initial} surface="scene-prose" />);
  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  const source = page.getByTestId(proseTestIds.sourceEditor('scene-prose'));
  await expect(source.locator('.cm-mention')).toHaveText('[Kent Beck]');
  await expect(source.locator('.cm-id').first()).toContainText(`kb:${KENT}`);
  await expect(source.locator('.cm-directive')).toContainText(`::beat{id="${BEAT}"}`);
  await expect(page.getByTestId('harness-markdown')).toHaveText(initial);
});

test('MarkdownSurface_WhenSummaryInSource_ShouldRefuseLineBreaks', async ({ mount, page }) => {
  await mount(<ProseHarness initial="Beck shipped it" summary surface="scene-summary" />);
  await page.getByTestId(proseTestIds.modeSource('scene-summary')).click();
  await page.getByTestId(proseTestIds.sourceEditor('scene-summary')).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.press('Enter');
  await page.keyboard.type('!');
  await expect(page.getByTestId('harness-markdown')).toHaveText('Beck shipped it!');
});

// Where inside the word the click lands is up to the browser; the unit tests in source/caretMap pin the exact mapping.
test('MarkdownSurface_WhenToggledMidText_ShouldKeepTheCaretInTheSameWord', async ({
  mount,
  page,
}) => {
  await mount(
    <ProseHarness initial={'First line.\n\nSecond **bold** word.'} surface="scene-prose" />,
  );
  await page.getByTestId(proseTestIds.editor).getByText('bold').click();
  await page.getByTestId(proseTestIds.modeSource('scene-prose')).click();
  await page.keyboard.type('Q');
  await expect(page.getByTestId('harness-markdown')).toHaveText(
    /^First line\.\n\nSecond \*\*b?o?Q[ol]?d?\*\* word\.$/,
  );
  await page.getByTestId(proseTestIds.modeRich('scene-prose')).click();
  await expect(page.getByTestId(proseTestIds.editor)).toBeFocused();
  await page.keyboard.type('R');
  await expect(page.getByTestId('harness-markdown')).toHaveText(
    /\*\*\w*Q\w*R\w*\*\*|\*\*\w*R\w*Q\w*\*\*/,
  );
});
