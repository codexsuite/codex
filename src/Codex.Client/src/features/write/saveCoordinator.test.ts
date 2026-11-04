import { describe, expect, it } from 'vitest';
import { createSaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { SaveCoordinator, worstState } from './saveCoordinator';

describe('worstState', () => {
  it('ranks conflict over unsaved over saving over saved', () => {
    expect(worstState([])).toBe('saved');
    expect(worstState(['saved', 'saving'])).toBe('saving');
    expect(worstState(['saving', 'unsaved', 'saved'])).toBe('unsaved');
    expect(worstState(['unsaved', 'conflict'])).toBe('conflict');
  });
});

describe('SaveCoordinator', () => {
  it('keeps "Updated elsewhere" from the realtime client through other editors until it is cleared', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    const one = coordinator.store('one');

    bar.setRemoteChange();
    expect(coordinator.remoteChanged()).toBe(true);
    one.set('unsaved');
    one.set('saving');
    one.set('saved');
    expect(bar.getState()).toBe('remoteChanged');

    coordinator.clearRemoteChange();
    expect(coordinator.remoteChanged()).toBe(false);
    expect(bar.getState()).toBe('saved');
    one.set('conflict');
    expect(bar.getState()).toBe('conflict');
  });

  it('stops following the bar once detached', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    coordinator.detach();

    bar.setRemoteChange();
    expect(coordinator.remoteChanged()).toBe(false);
  });

  it('publishes the worst state of its editors to the bar', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    const one = coordinator.store('one');
    const two = coordinator.store('two');

    one.set('unsaved');
    two.set('saving');
    expect(bar.getState()).toBe('unsaved');
    one.set('saved');
    expect(bar.getState()).toBe('saving');
    two.set('saved');
    expect(bar.getState()).toBe('saved');
  });

  it('shows a conflict on the bar and lists the scene', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    coordinator.store('one').set('conflict');
    coordinator.store('two').set('unsaved');

    expect(bar.getState()).toBe('conflict');
    expect(coordinator.conflicts()).toEqual(['one']);
  });

  it('forgets a released editor and goes quiet once detached', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    const one = coordinator.store('one');
    one.set('unsaved');
    coordinator.release('one');
    expect(bar.getState()).toBe('saved');

    one.set('unsaved');
    coordinator.detach();
    one.set('saving');
    expect(bar.getState()).toBe('saved');
  });

  it('counts the edits waiting, lists the refused scenes and tells a stuck edit from a pending one', () => {
    const bar = createSaveStatusStore();
    const coordinator = new SaveCoordinator(bar);
    const one = coordinator.store('one');
    const two = coordinator.store('two');

    one.set('unsaved');
    expect(coordinator.hasStuck()).toBe(false);
    one.set('offline');
    two.set('retry');

    expect(bar.getState()).toBe('retry');
    expect(bar.getWaiting()).toBe(2);
    expect(coordinator.refused()).toEqual(['two']);
    expect(coordinator.hasStuck()).toBe(true);
    two.set('saved');
    expect(bar.getState()).toBe('offline');
    expect(bar.getWaiting()).toBe(1);
  });
});
