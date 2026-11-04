import { expect, test } from '@playwright/experimental-ct-react';
import { ProseHarness } from '../test-support/ProseHarness';
import { proseTestIds } from './testIds';

const KENT = '0199c0de-0000-7000-8000-000b0000000b';
const BEAT = '0199c0de-0000-7000-8000-000700000003';

test('ProseEditor_WhenAtTyped_ShouldInsertMention', async ({ mount, page }) => {
  await mount(<ProseHarness />);
  const editor = page.getByTestId(proseTestIds.editor);
  await editor.click();
  await editor.pressSequentially('Early on @Ken');
  await expect(page.getByTestId(proseTestIds.mentionPicker)).toBeVisible();
  await expect(page.getByTestId(proseTestIds.mentionOption(KENT))).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('harness-markdown')).toContainText(`[Kent Beck](kb:${KENT})`);
  await expect(page.getByTestId(proseTestIds.mentionPicker)).toHaveCount(0);
});

// The stored text has no trailing newline, which the serializer would add: an emit on mount would show as one.
test('ProseEditor_WhenMounted_ShouldNotEmitAChange', async ({ mount, page }) => {
  await mount(<ProseHarness initial="Opening line." />);
  await expect(page.getByTestId(proseTestIds.editor)).toContainText('Opening line.');
  expect(await page.getByTestId('harness-markdown').textContent()).toBe('Opening line.');
});

test('ProseEditor_WhenDirectiveBlocks_ShouldRenderCardsAndKeepMarkdown', async ({
  mount,
  page,
}) => {
  const initial = `Opening line.\n\n::beat{id="${BEAT}"}\n\n:::aside\nTable rule.\n:::\n\n::future{x="y"}\n`;
  await mount(<ProseHarness initial={initial} />);
  await expect(page.getByTestId(proseTestIds.directiveCard('beat'))).toBeVisible();
  await expect(page.getByTestId(proseTestIds.containerCard('aside'))).toBeVisible();
  await expect(page.getByTestId(proseTestIds.opaqueCard)).toBeVisible();
  const editor = page.getByTestId(proseTestIds.editor);
  await editor.getByText('Opening line.').click();
  await page.keyboard.press('End');
  await page.keyboard.type(' More.');
  await expect(page.getByTestId('harness-markdown')).toHaveText(
    initial.replace('Opening line.', 'Opening line. More.'),
  );
});

test('ProseEditor_WhenDocumentHoldsRichMarkdown_ShouldRenderAndAcceptTyping', async ({
  mount,
  page,
}) => {
  const initial = [
    'Intro ~~old~~ [site](https://example.com) `\\textsc{x}`{=latex} end.',
    '| A | B |\n|:--|--:|\n| one | two |',
    '---',
    '- Parent\n  - Child',
    '<!-- note -->',
  ].join('\n\n');
  await mount(<ProseHarness initial={initial} />);
  const editor = page.getByTestId(proseTestIds.editor);
  await expect(editor.locator('table td', { hasText: 'one' })).toBeVisible();
  await expect(editor.locator('s')).toHaveText('old');
  await expect(editor.locator('a')).toHaveAttribute('href', 'https://example.com');
  await expect(editor.locator('hr')).toBeVisible();
  await expect(page.getByTestId(proseTestIds.rawLatexInline)).toHaveText('\\textsc{x}');
  await expect(page.getByTestId(proseTestIds.opaqueCard)).toBeVisible();

  await editor.locator('table td', { hasText: 'one' }).click();
  await page.keyboard.press('End');
  await page.keyboard.type('!');
  await editor.getByText('Child').click();
  await page.keyboard.press('End');
  await page.keyboard.type(' kid');
  await expect(page.getByTestId('harness-markdown')).toHaveText(
    initial
      .replace(
        '| A | B |\n|:--|--:|\n| one | two |',
        '| A    | B   |\n|:-----|----:|\n| one! | two |',
      )
      .replace('Child', 'Child kid') + '\n',
  );
});

test('SummaryEditor_WhenTyped_ShouldEmitMarkdown', async ({ mount, page }) => {
  await mount(<ProseHarness summary />);
  const editor = page.getByTestId(proseTestIds.summaryEditor);
  await editor.click();
  await page.keyboard.type('Beck ');
  await page.keyboard.press('Control+b');
  await page.keyboard.type('shipped');
  await page.keyboard.press('Control+b');
  await page.keyboard.type(' it');
  await expect(page.getByTestId('harness-markdown')).toHaveText('Beck **shipped** it');
});

test('SlashMenu_WhenItemDisabled_ShouldNotRun', async ({ mount, page }) => {
  await mount(<ProseHarness />);
  const editor = page.getByTestId(proseTestIds.editor);
  await editor.click();
  await editor.pressSequentially('Intro /Dis');
  await expect(page.getByTestId(proseTestIds.slashItem('disabled'))).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await page.keyboard.press('Enter');
  await page.getByTestId(proseTestIds.slashItem('disabled')).click({ force: true });
  await expect(page.getByTestId('harness-runs')).toHaveText('');
  await expect(page.getByTestId(proseTestIds.slashMenu)).toBeVisible();

  await page.keyboard.press('Control+a');
  await page.keyboard.press('Backspace');
  await editor.pressSequentially('/Ena');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('harness-runs')).toHaveText('enabled');
});
