import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChangeSender } from './ChangeSender';

describe('ChangeSender', () => {
  const send = vi.fn<(sceneId: string, markdown: string) => void>();
  let sender: ChangeSender;

  beforeEach(() => {
    vi.useFakeTimers();
    send.mockClear();
    sender = new ChangeSender(send, 300);
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('schedule_WhenTypingKeepsGoing_ShouldSendOnlyTheLatestAfterThePause', () => {
    sender.schedule('s1', 'a');
    vi.advanceTimersByTime(200);
    sender.schedule('s1', 'ab');
    vi.advanceTimersByTime(200);
    expect(send).not.toHaveBeenCalled();

    vi.advanceTimersByTime(100);

    expect(send).toHaveBeenCalledExactlyOnceWith('s1', 'ab');
  });

  it('schedule_WhenTwoScenes_ShouldKeepTheirChangesApart', () => {
    sender.schedule('s1', 'one');
    sender.schedule('s2', 'two');

    vi.advanceTimersByTime(300);

    expect(send.mock.calls).toEqual([
      ['s1', 'one'],
      ['s2', 'two'],
    ]);
  });

  it('flush_WhenSomethingWaits_ShouldSendItAtOnceAndOnlyOnce', () => {
    sender.schedule('s1', 'typed');

    sender.flush('s1');
    vi.advanceTimersByTime(1000);

    expect(send).toHaveBeenCalledExactlyOnceWith('s1', 'typed');
  });

  it('flush_WhenNothingWaits_ShouldSendNothing', () => {
    sender.flush('s1');

    expect(send).not.toHaveBeenCalled();
  });

  it('flushAll_WhenSeveralWait_ShouldSendEachOnce', () => {
    sender.schedule('s1', 'a');
    sender.schedule('s2', 'b');

    sender.flushAll();
    vi.advanceTimersByTime(1000);

    expect(send.mock.calls).toEqual([
      ['s1', 'a'],
      ['s2', 'b'],
    ]);
  });

  it('cancel_WhenSomethingWaits_ShouldNeverSendIt', () => {
    sender.schedule('s1', 'typed');

    sender.cancel('s1');
    vi.advanceTimersByTime(1000);

    expect(send).not.toHaveBeenCalled();
  });

  it('flushAllExcept_WhenSomeAreSkipped_ShouldSendTheOthersAndDropTheSkipped', () => {
    sender.schedule('s1', 'a');
    sender.schedule('s2', 'b');

    sender.flushAllExcept(new Set(['s2']));
    vi.advanceTimersByTime(1000);

    expect(send).toHaveBeenCalledExactlyOnceWith('s1', 'a');
  });
});
