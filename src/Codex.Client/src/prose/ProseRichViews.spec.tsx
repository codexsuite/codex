import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import { ProseHarness } from '../test-support/ProseHarness';
import { proseTestIds } from './testIds';

const TABLE = '| A | B |\n|---|---|\n| one | two |\n| three | four |';

const markdown = (page: Page) => page.getByTestId('harness-markdown');
const editorOf = (page: Page) => page.getByTestId(proseTestIds.editor);
const cell = (page: Page, text: string) =>
  editorOf(page).locator('td, th', { hasText: text }).first();

// The browser moves the caret and the editor learns of it a moment later: wait until both agree before typing.
async function settled(page: Page) {
  await page.waitForFunction(() => {
    const view = (
      document.querySelector('[data-testid="prose-editor"]') as unknown as {
        editor: {
          view: { posAtDOM(n: Node, o: number): number };
          state: { selection: { anchor: number; head: number } };
        };
      }
    ).editor;
    const dom = window.getSelection();
    return (
      dom?.anchorNode != null &&
      dom.focusNode != null &&
      view.view.posAtDOM(dom.anchorNode, dom.anchorOffset) === view.state.selection.anchor &&
      view.view.posAtDOM(dom.focusNode, dom.focusOffset) === view.state.selection.head
    );
  });
}

async function caretIn(page: Page, text: string) {
  await cell(page, text).click();
  await settled(page);
}

// Under load the editor can sync the click's caret back to the page after End has moved it, so press End until the
// editor's caret is at the end of the cell.
async function caretAtEndOf(page: Page, text: string) {
  await caretIn(page, text);
  await expect(async () => {
    await page.keyboard.press('End');
    await settled(page);
    const atEnd = await page.evaluate(() => {
      const { $head } = (
        document.querySelector('[data-testid="prose-editor"]') as unknown as {
          editor: {
            state: {
              selection: { $head: { parentOffset: number; parent: { content: { size: number } } } };
            };
          };
        }
      ).editor.state.selection;
      return $head.parentOffset === $head.parent.content.size;
    });
    expect(atEnd).toBe(true);
  }).toPass();
}

test('Table_WhenInsertedFromSlashMenu_ShouldBeAHeaderPlusTwoRowsAndTypeable', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial="Intro." />);
  const editor = editorOf(page);
  await editor.click();
  await settled(page);
  await page.keyboard.press('Control+End');
  await settled(page);
  await page.keyboard.press('Enter');
  await settled(page);
  await page.keyboard.type('/Tab');
  await page.keyboard.press('Enter');
  await expect(editor.locator('table')).toHaveCount(1);
  await expect(editor.locator('th')).toHaveCount(2);
  await expect(editor.locator('td')).toHaveCount(4);
  await page.keyboard.type('Name');
  await page.keyboard.press('Enter');
  await page.keyboard.type('Role');
  await page.keyboard.press('Enter');
  await page.keyboard.type('Kent');
  await expect(markdown(page)).toHaveText(
    'Intro.\n\n| Name | Role |\n|------|------|\n| Kent |      |\n|      |      |\n',
  );
});

test('Table_WhenToolbarRowAndColumnOps_ShouldWriteACanonicalPipeTable', async ({ mount, page }) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretIn(page, 'two');
  await page.getByTestId(proseTestIds.tableOp('addRowBefore')).click();
  await page.getByTestId(proseTestIds.tableOp('addColumnAfter')).click();
  await expect(editorOf(page).locator('tr')).toHaveCount(4);
  await expect(editorOf(page).locator('th')).toHaveCount(3);
  await editorOf(page).locator('th').nth(2).click();
  await page.keyboard.type('C');
  await expect(markdown(page)).toHaveText(
    '| A     | B    | C |\n|-------|------|---|\n|       |      |   |\n| one   | two  |   |\n| three | four |   |\n',
  );
});

test('Table_WhenRowColumnAndTableDeleted_ShouldShrinkThenGoAway', async ({ mount, page }) => {
  await mount(<ProseHarness initial={`${TABLE}\n\nAfter.`} />);
  await caretIn(page, 'three');
  await page.getByTestId(proseTestIds.tableOp('deleteRow')).click();
  await expect(editorOf(page).locator('tr')).toHaveCount(2);
  await caretIn(page, 'two');
  await page.getByTestId(proseTestIds.tableOp('deleteColumn')).click();
  await expect(markdown(page)).toHaveText('| A   |\n|-----|\n| one |\n\nAfter.\n');
  await page.getByTestId(proseTestIds.tableOp('deleteTable')).click();
  await expect(editorOf(page).locator('table')).toHaveCount(0);
  await expect(markdown(page)).toHaveText('After.\n');
});

test('Table_WhenCaretInHeaderRow_ShouldNotAddAboveOrDeleteTheRow', async ({ mount, page }) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretIn(page, 'A');
  await expect(page.getByTestId(proseTestIds.tableOp('addRowBefore'))).toBeDisabled();
  await expect(page.getByTestId(proseTestIds.tableOp('deleteRow'))).toBeDisabled();
  await caretIn(page, 'one');
  await expect(page.getByTestId(proseTestIds.tableOp('addRowBefore'))).toBeEnabled();
});

test('Table_WhenColumnAligned_ShouldWriteTheAlignmentRow', async ({ mount, page }) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretIn(page, 'two');
  await page.getByTestId(proseTestIds.tableAlign('right')).click();
  await caretIn(page, 'A');
  await page.getByTestId(proseTestIds.tableAlign('center')).click();
  await expect(markdown(page)).toHaveText(
    '| A     |    B |\n|:-----:|-----:|\n| one   |  two |\n| three | four |\n',
  );
  await expect(cell(page, 'two')).toHaveCSS('text-align', 'right');
  await caretIn(page, 'two');
  await page.getByTestId(proseTestIds.tableAlign('default')).click();
  await expect(cell(page, 'two')).not.toHaveCSS('text-align', 'right');
});

test('Table_WhenAddColumnOnAlignedTable_ShouldKeepTheOtherAlignments', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'| A | B |\n|:--|--:|\n| one | two |'} />);
  await caretIn(page, 'one');
  await page.getByTestId(proseTestIds.tableOp('addColumnBefore')).click();
  await caretIn(page, 'two');
  await page.keyboard.type('!');
  const text = (await markdown(page).textContent()) ?? '';
  expect(text.split('\n')[1]).toMatch(/^\|-+\|:-+\|-+:\|$/);
});

test('Table_WhenEnterAndShiftEnterInCell_ShouldMoveOnAndNeverBreak', async ({ mount, page }) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretAtEndOf(page, 'one');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.type('X');
  await expect(markdown(page)).toHaveText(
    '| A     | B    |\n|-------|------|\n| one   | X    |\n| three | four |\n',
  );
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.type('Y');
  await expect(markdown(page)).toHaveText(
    '| A     | B |\n|-------|---|\n| one   | X |\n| three | Y |\n',
  );
  await expect(editorOf(page).locator('table')).toHaveCount(1);
});

test('Table_WhenEnterPicksFromOpenMentionPicker_ShouldInsertMentionAndStayInTheCell', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretAtEndOf(page, 'one');
  await page.keyboard.type(' @Beck');
  await expect(page.getByTestId(proseTestIds.mentionPicker)).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(proseTestIds.mentionPicker)).toHaveCount(0);
  await expect(cell(page, 'one')).toContainText('Kent Beck');
  await expect(markdown(page)).toContainText(
    '| one [Kent Beck](kb:0199c0de-0000-7000-8000-000b0000000b) | two  |',
  );
});

test('Table_WhenBlocksPastedIntoCell_ShouldBeFlattenedToText', async ({ mount, page }) => {
  await mount(<ProseHarness initial={TABLE} />);
  await caretAtEndOf(page, 'one');
  await page.evaluate(() => {
    const data = new DataTransfer();
    data.setData('text/html', '<p>first</p><p>second<br>third</p>');
    data.setData('text/plain', 'first\n\nsecond\nthird');
    document
      .querySelector('[data-testid="prose-editor"]')
      ?.dispatchEvent(
        new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }),
      );
  });
  await expect(editorOf(page).locator('table')).toHaveCount(1);
  await expect(editorOf(page).locator('td p')).toHaveCount(4);
  await expect(cell(page, 'one')).toHaveText('onefirst second third');
  expect(await markdown(page).textContent()).toContain('| onefirst second third |');
});

test('RawLatexBlock_WhenEdited_ShouldWriteTheFencedSource', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'Before.\n\n```{=latex}\n\\vspace{1em}\n```\n\nAfter.'} />);
  const card = page.getByTestId(proseTestIds.rawLatexCard);
  await expect(card).toContainText('Raw LaTeX, template-specific');
  const code = card.getByTestId(proseTestIds.rawLatexEditor);
  await code.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type('\n\\newpage');
  await expect(markdown(page)).toHaveText(
    'Before.\n\n```{=latex}\n\\vspace{1em}\n\\newpage\n```\n\nAfter.\n',
  );
});

test('RawLatexBlock_WhenEditorGoesReadOnly_ShouldStopAcceptingTyping', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'```{=latex}\n\\a\n```'} />);
  const code = page.getByTestId(proseTestIds.rawLatexEditor);
  await page.getByTestId('harness-toggle-read-only').click();
  await expect(code).toHaveAttribute('contenteditable', 'false');
  await code.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type('bc');
  await expect(markdown(page)).toHaveText('```{=latex}\n\\a\n```');

  await page.getByTestId('harness-toggle-read-only').click();
  await expect(code).toHaveAttribute('contenteditable', 'true');
  await code.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type('bc');
  await expect(markdown(page)).toContainText('\\abc');
});

test('RawLatexBlock_WhenUndone_ShouldRestoreTheSource', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'```{=latex}\n\\a\n```'} />);
  const code = page.getByTestId(proseTestIds.rawLatexEditor);
  await code.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type('bc');
  await expect(markdown(page)).toContainText('\\abc');
  await page.keyboard.press('Control+z');
  await expect(markdown(page)).not.toContainText('\\abc');
  await expect(code).toContainText('\\a');
  await expect(code).not.toContainText('\\abc');
});

test('RawLatexBlock_WhenArrowAtEnds_ShouldMoveTheCaretOutOfTheBlock', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'Before.\n\n```{=latex}\n\\a\n```\n\nAfter.'} />);
  const code = page.getByTestId(proseTestIds.rawLatexEditor);
  await code.click();
  await page.keyboard.press('Control+End');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.type('X');
  await expect(markdown(page)).toContainText('XAfter.');
  await code.click();
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('ArrowUp');
  await page.keyboard.type('Y');
  await expect(markdown(page)).toContainText('Before.Y');
});

test('RawLatexInline_WhenChipEdited_ShouldWriteTheNewSource', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'Go `\\textsc{x}`{=latex} on.'} />);
  await page.getByTestId(proseTestIds.rawLatexInline).click();
  const input = page.getByTestId(proseTestIds.rawLatexInlineInput);
  await expect(input).toHaveValue('\\textsc{x}');
  await input.fill('\\textsc{y}');
  await page.keyboard.press('Enter');
  await expect(markdown(page)).toHaveText('Go `\\textsc{y}`{=latex} on.\n');
  await expect(page.getByTestId(proseTestIds.rawLatexInline)).toHaveText('\\textsc{y}');
});

test('RawLatexInline_WhenClickedAndBlurredWithoutAnEdit_ShouldLeaveTheDocumentAlone', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={'Go `  x  `{=latex} on.'} />);
  await page.getByTestId(proseTestIds.rawLatexInline).click();
  await expect(page.getByTestId(proseTestIds.rawLatexInlineInput)).toHaveValue(' x ');
  await editorOf(page).getByText('Go').click();
  await expect(page.getByTestId(proseTestIds.rawLatexInlineInput)).toHaveCount(0);
  await expect(markdown(page)).toHaveText('Go `  x  `{=latex} on.');
});

test('RawLatexInline_WhenSourceHasANewline_ShouldNotBeEditableInPlace', async ({ mount, page }) => {
  await mount(<ProseHarness initial={'Go `a\nb`{=latex} on.'} />);
  await page.getByTestId(proseTestIds.rawLatexInline).click();
  await expect(page.getByTestId(proseTestIds.rawLatexInlineInput)).toHaveCount(0);
  await expect(markdown(page)).toHaveText('Go `a\nb`{=latex} on.');
});

test('RawLatexInline_WhenEscapePressedAfterTyping_ShouldDiscardTheDraft', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={'Go `\\textsc{x}`{=latex} on.'} />);
  await page.getByTestId(proseTestIds.rawLatexInline).click();
  await page.getByTestId(proseTestIds.rawLatexInlineInput).fill('\\other');
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(proseTestIds.rawLatexInlineInput)).toHaveCount(0);
  await expect(page.getByTestId(proseTestIds.rawLatexInline)).toHaveText('\\textsc{x}');
  await expect(markdown(page)).toHaveText('Go `\\textsc{x}`{=latex} on.');
});

test('RawLatexInline_WhenSelectedAndEnter_ShouldEditAndEmptyingDeletesIt', async ({
  mount,
  page,
}) => {
  await mount(<ProseHarness initial={'Go `\\textsc{x}`{=latex} on.'} />);
  await editorOf(page).getByText('Go').click();
  // Under load the editor can sync the click's caret back after the keys have moved it, so walk from Home to the
  // chip until it is the selected node.
  await expect(async () => {
    await page.keyboard.press('Home');
    await settled(page);
    for (let i = 0; i < 4; i++) {
      await page.keyboard.press('ArrowRight');
      await settled(page);
    }
    await expect(editorOf(page).locator('.ProseMirror-selectednode')).toHaveCount(1, {
      timeout: 1000,
    });
  }).toPass();
  await page.keyboard.press('Enter');
  const input = page.getByTestId(proseTestIds.rawLatexInlineInput);
  await expect(input).toBeVisible();
  await input.fill('');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(proseTestIds.rawLatexInline)).toHaveCount(0);
  await expect(markdown(page)).toHaveText('Go  on.\n');
});

test('SlashMenu_WhenDividerChosen_ShouldInsertARule', async ({ mount, page }) => {
  await mount(<ProseHarness initial="Top." />);
  const editor = editorOf(page);
  await editor.click();
  await settled(page);
  await page.keyboard.press('Control+End');
  await settled(page);
  await page.keyboard.press('Enter');
  await settled(page);
  await page.keyboard.type('/Divi');
  await page.keyboard.press('Enter');
  await expect(editor.locator('hr')).toHaveCount(1);
  await page.keyboard.type('Below.');
  await expect(markdown(page)).toHaveText('Top.\n\n---\n\nBelow.\n');
});

test('Strike_WhenShortcutPressed_ShouldWrapSelectionInTildes', async ({ mount, page }) => {
  await mount(<ProseHarness initial="Drop this now." />);
  await editorOf(page).click();
  await settled(page);
  await page.keyboard.press('Home');
  await settled(page);
  for (let i = 0; i < 4; i++) await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(() => page.evaluate(() => String(window.getSelection()))).toBe('Drop');
  await settled(page);
  await page.keyboard.press('Control+Shift+x');
  await expect(editorOf(page).locator('s')).toHaveText('Drop');
  await expect(markdown(page)).toHaveText('~~Drop~~ this now.\n');
});
