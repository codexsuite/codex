import { render } from '@testing-library/react-native';
import { useState } from 'react';
import { liveAutosaves } from './liveAutosaves';
import { useAutosaveController, type BookAutosave } from './useAutosaveController';

const save = jest.fn(async (_value: string, version: number) => ({ version }));

function Probe({
  onResult,
  beforeEffects,
}: {
  onResult: (result: BookAutosave<string>) => void;
  beforeEffects?: () => void;
}) {
  const result = useAutosaveController<string>('book-1', 'kb', 'e1', {
    value: 'server',
    version: 0,
    save,
  });
  onResult(result);
  // Runs while this screen renders, after it adopted a live controller and before its effects: the moment in which the
  // screen that left can retire the controller.
  useState(() => beforeEffects?.());
  return null;
}

describe('useAutosaveController', () => {
  beforeEach(() => save.mockClear());

  it('Mount_WhenTheAdoptedControllerWasRetiredBeforeTheEffects_ShouldStartAFreshOneAndSave', async () => {
    // The first screen leaves with an edit whose save never comes back, so its controller stays alive in the registry.
    let firstResult: BookAutosave<string> | undefined;
    const first = await render(<Probe onResult={(r) => (firstResult = r)} />);
    save.mockImplementationOnce(() => new Promise(() => undefined));
    firstResult?.sync('typed', 0);
    await first.unmount();
    const old = liveAutosaves.get('book-1', 'kb:e1');
    expect(old).toBeDefined();
    // Adopt the controller the first screen left, then let the first screen's leave retire it.
    let result: BookAutosave<string> | undefined;
    await render(
      <Probe
        onResult={(r) => (result = r)}
        beforeEffects={() => {
          old?.controller.dispose();
          if (old) liveAutosaves.remove('book-1', 'kb:e1', old);
        }}
      />,
    );

    const live = liveAutosaves.get('book-1', 'kb:e1');
    expect(live).toBeDefined();
    expect(live).not.toBe(old);
    expect(result?.controller).not.toBe(old?.controller);
    result?.sync('typed again', 0);
    await result?.controller.flush();
    expect(save).toHaveBeenCalledWith('typed again', 0);
  });
});
