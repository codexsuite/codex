import { editingHarnessIds } from '../test-support/editingHarnessIds';
import { test, expect } from '@playwright/experimental-ct-react';
import { AutosaveHarness, LabelPickerHarness, ToastHarness } from '../test-support/EditingHarness';
import { editingTestIds } from './testIds';
import { TOAST_TIMEOUT_MS } from '@codex/core/editing/toastStore';

const labels = [
  { id: 'a', name: 'Draft', tone: 'info' as const },
  { id: 'b', name: 'Needs work', tone: 'warn' as const },
];

test.describe('autosave', () => {
  test.beforeEach(async ({ page }) => {
    await page.clock.install();
  });

  test('Autosave_WhenEdited_ShouldSaveAfterDelay', async ({ mount, page }) => {
    const component = await mount(<AutosaveHarness behavior="ok" />);
    // Paused, so only the fast-forwards move time: on a busy runner the real time spent between them would otherwise
    // push 2900 ms past the 3 s delay.
    await page.clock.pauseAt((await page.evaluate(() => Date.now())) + 1000);
    await component.getByTestId(editingHarnessIds.input).fill('hello');
    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Unsaved');

    await page.clock.fastForward(2900);
    await expect(component.getByTestId(editingHarnessIds.saves)).toHaveText('0');
    await page.clock.fastForward(200);

    await expect(component.getByTestId(editingHarnessIds.saves)).toHaveText('1');
    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Saved');
  });

  test('Autosave_WhenBlurred_ShouldSaveNow', async ({ mount }) => {
    const component = await mount(<AutosaveHarness behavior="ok" />);
    const input = component.getByTestId(editingHarnessIds.input);
    await input.fill('hello');

    await input.blur();

    await expect(component.getByTestId(editingHarnessIds.saves)).toHaveText('1');
    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Saved');
  });

  test('Autosave_WhenConflict_ShouldShowConflict', async ({ mount, page }) => {
    const component = await mount(<AutosaveHarness behavior="conflict" />);
    await component.getByTestId(editingHarnessIds.input).fill('hello');

    await page.clock.fastForward(3100);

    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Conflict · Reload');
    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveAttribute(
      'data-state',
      'conflict',
    );
    await page.clock.fastForward(10_000);
    await expect(component.getByTestId(editingHarnessIds.saves)).toHaveText('1');
  });

  test('Autosave_WhenOffline_ShouldShowUnsaved', async ({ mount, page }) => {
    const component = await mount(<AutosaveHarness behavior="offline" />);
    await component.getByTestId(editingHarnessIds.input).fill('hello');

    await page.clock.fastForward(3100);

    await expect(component.getByTestId(editingHarnessIds.saves)).toHaveText('1');
    await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Unsaved');
  });
});

test('SaveStatus_WhenRemoteChange_ShouldShowReload', async ({ mount }) => {
  const component = await mount(<AutosaveHarness behavior="ok" />);

  await component.getByTestId(editingHarnessIds.reload).click();

  await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText(
    'Updated elsewhere · Reload',
  );
  await component.getByTestId(editingTestIds.saveStatusReload).click();
  await expect(component.getByTestId(editingTestIds.saveStatus)).toHaveText('Saved');
});

test('LabelPicker_WhenToggled_ShouldEmit', async ({ mount }) => {
  const component = await mount(<LabelPickerHarness labels={labels} />);

  await component.getByTestId(editingTestIds.labelOption('a')).click();
  await component.getByTestId(editingTestIds.labelOption('b')).click();
  await expect(component.getByTestId(editingHarnessIds.selected)).toHaveText('a,b');

  await component.getByTestId(editingTestIds.labelOption('a')).click();
  await expect(component.getByTestId(editingHarnessIds.selected)).toHaveText('b');
  await expect(component.getByTestId(editingTestIds.labelOption('b'))).toHaveAttribute(
    'aria-checked',
    'true',
  );
});

test('LabelPicker_WhenToneUnknown_ShouldOfferItWithAHollowDot', async ({ mount }) => {
  const component = await mount(
    <LabelPickerHarness labels={[...labels, { id: 'c', name: 'Teal', tone: null }]} />,
  );

  const unknown = component.getByTestId(editingTestIds.labelOption('c'));
  await expect(unknown).toHaveAttribute('data-tone', 'unknown');
  await unknown.click();
  await expect(component.getByTestId(editingHarnessIds.selected)).toHaveText('c');
});

test('Toast_WhenUndoClicked_ShouldCallUndo', async ({ mount, page }) => {
  const component = await mount(<ToastHarness withUndo />);
  await component.getByTestId(editingHarnessIds.show).click();
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Archived scene');

  await page.getByTestId(editingTestIds.toastUndo).click();

  await expect(component.getByTestId(editingHarnessIds.undone)).toHaveText('1');
  await expect(page.getByTestId(editingTestIds.toast)).toHaveCount(0);
});

test('Toast_WhenTimeoutPasses_ShouldDisappearWithoutTakingFocus', async ({ mount, page }) => {
  await page.clock.install();
  const component = await mount(<ToastHarness withUndo={false} />);
  const show = component.getByTestId(editingHarnessIds.show);
  await show.focus();
  await show.click();
  await expect(page.getByTestId(editingTestIds.toast)).toBeVisible();
  await expect(show).toBeFocused();
  await expect(page.getByTestId(editingTestIds.toastUndo)).toHaveCount(0);

  await page.clock.fastForward(TOAST_TIMEOUT_MS + 100);

  await expect(page.getByTestId(editingTestIds.toast)).toHaveCount(0);
});
