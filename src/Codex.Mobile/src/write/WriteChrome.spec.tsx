import { fireEvent, screen, userEvent } from '@testing-library/react-native';
import type { EditorCommand, PageMessage } from '@codex/core/editing/editorBridge';
import { writeTestIds } from './testIds';
import { openWrite } from './test-support/openWrite';

type State = Extract<PageMessage, { type: 'state' }>;
const state = (overrides: Partial<State> = {}): State => ({
  type: 'state',
  bold: false,
  italic: false,
  quote: false,
  canUndo: false,
  canRedo: false,
  ...overrides,
});

async function editing() {
  const opened = await openWrite();
  await opened.page.ready();
  await opened.page.waitForLoads(1);
  await opened.page.say({ type: 'focus', sceneId: 's2' });
  return opened;
}

const commandsSent = (page: Awaited<ReturnType<typeof editing>>['page']) =>
  page.sent().flatMap((m) => (m.type === 'command' ? [m.command] : []));

describe('Write: the accessory bar', () => {
  it('Bar_ShouldAppearWhileThePageHasFocusAndGoWhenItBlurs', async () => {
    const { page } = await openWrite();
    await page.ready();
    await page.waitForLoads(1);
    expect(screen.queryByTestId(writeTestIds.accessoryBar)).toBeNull();

    await page.say({ type: 'focus', sceneId: 's1' });
    expect(screen.getByTestId(writeTestIds.accessoryBar)).toBeOnTheScreen();

    await page.say({ type: 'blur', sceneId: 's1' });
    expect(screen.queryByTestId(writeTestIds.accessoryBar)).toBeNull();
  });

  it.each<[string, EditorCommand]>([
    ['slash', 'slash'],
    ['mention', 'mention'],
    ['beat', 'beat'],
    ['newScene', 'newScene'],
    ['bold', 'bold'],
    ['italic', 'italic'],
    ['quote', 'quote'],
    ['blur', 'blur'],
  ])('Key_%s_ShouldSendTheMatchingCommand', async (key, command) => {
    const { page } = await editing();

    await fireEvent.press(screen.getByTestId(writeTestIds.accessory(key)));

    expect(commandsSent(page)).toEqual([command]);
  });

  it('Keys_ShouldShowTheMarksTheSelectionIsIn', async () => {
    const { page } = await editing();

    await page.say(state({ bold: true, quote: true }));

    const selected = (key: string) =>
      screen.getByTestId(writeTestIds.accessory(key)).props.accessibilityState.selected;
    expect(selected('bold')).toBe(true);
    expect(selected('italic')).toBe(false);
    expect(selected('quote')).toBe(true);

    await page.say(state({ italic: true }));
    expect(selected('bold')).toBe(false);
    expect(selected('italic')).toBe(true);
  });
});

describe('Write: editing chrome', () => {
  it('Editing_ShouldReplaceThePreviousAndNextButtonsWithUndoAndRedoAndNameTheScene', async () => {
    const { page } = await editing();
    await page.say(state({ canUndo: true }));

    expect(screen.queryByTestId(writeTestIds.pillPrevious)).toBeNull();
    expect(screen.queryByTestId(writeTestIds.pillNext)).toBeNull();
    expect(screen.getByTestId(writeTestIds.undo)).toBeOnTheScreen();
    expect(screen.getByTestId(writeTestIds.pillLabel)).toHaveTextContent(
      '1 · An Empty Seat · Scene 2',
    );

    await page.say({ type: 'blur', sceneId: 's2' });
    expect(screen.getByTestId(writeTestIds.pillNext)).toBeOnTheScreen();
    expect(screen.queryByTestId(writeTestIds.undo)).toBeNull();
  });

  it('UndoAndRedo_ShouldBeEnabledFromThePagesState', async () => {
    const { page } = await editing();
    const user = userEvent.setup();

    await user.press(screen.getByTestId(writeTestIds.undo));
    await user.press(screen.getByTestId(writeTestIds.redo));
    expect(commandsSent(page)).toEqual([]);

    await page.say(state({ canUndo: true, canRedo: false }));
    await user.press(screen.getByTestId(writeTestIds.undo));
    await user.press(screen.getByTestId(writeTestIds.redo));
    expect(commandsSent(page)).toEqual(['undo']);

    await page.say(state({ canUndo: true, canRedo: true }));
    await user.press(screen.getByTestId(writeTestIds.redo));
    expect(commandsSent(page)).toEqual(['undo', 'redo']);
  });
});
