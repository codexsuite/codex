import { createSaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { describe, expect, it } from 'vitest';
import { createBookSaveStatus, worstState } from './saveStatus';

describe('worstState', () => {
  it.each([
    [[], 'saved'],
    [['saved', 'saving'], 'saving'],
    [['saving', 'unsaved'], 'unsaved'],
    [['unsaved', 'offline'], 'offline'],
    [['offline', 'retry'], 'retry'],
    [['retry', 'conflict', 'saved'], 'conflict'],
  ] as const)('WhenStatesAre_%j_ShouldBe_%s', (states, expected) => {
    expect(worstState(states)).toBe(expected);
  });
});

function setup() {
  const bar = createSaveStatusStore();
  const status = createBookSaveStatus(bar);
  return { bar, status, book: status.forBook('book-a') };
}

describe('createBookSaveStatus', () => {
  it('ShouldShowTheWorstSourceOnTheBarAndClearItWhenThatSourceSaves', () => {
    const { bar, book } = setup();
    const a = book.store('write:a');
    const b = book.store('write:b');

    a.set('unsaved');
    b.set('saved');
    expect(bar.getState()).toBe('unsaved');

    b.set('saving');
    expect(bar.getState()).toBe('unsaved');

    a.set('saved');
    expect(bar.getState()).toBe('saving');

    b.set('saved');
    expect(bar.getState()).toBe('saved');
  });

  it('ShouldGiveTheSameStoreForTheSameKeyAndForgetAReleasedSource', () => {
    const { bar, book } = setup();

    expect(book.store('write:a')).toBe(book.store('write:a'));
    book.store('write:a').set('conflict');
    expect(bar.getState()).toBe('conflict');

    book.release('write:a', book.store('write:a'));
    expect(bar.getState()).toBe('saved');
  });

  it('WhenAWriteSaveFailsAndAScenePlanSaveSucceeds_ShouldKeepTheDotOnTheFailure', () => {
    const { bar, book } = setup();
    book.store('write:s1').set('retry');

    const title = book.store('plan:s2:title');
    title.set('unsaved');
    title.set('saving');
    title.set('saved');

    expect(bar.getState()).toBe('retry');
  });

  it('WhenAnEntrySummaryIsOfflineAndWriteChangesChapter_ShouldKeepOffline', () => {
    const { bar, book } = setup();
    book.store('kb:e1').set('offline');

    // Write's chapter change: a scene is released, another opens and saves.
    const first = book.store('write:s1');
    first.set('saved');
    book.release('write:s1', first);
    book.store('write:s2').set('saved');

    expect(bar.getState()).toBe('offline');
  });

  it('WhenAnotherBookIsOpened_ShouldNotColourItsDotWithTheOldBooksUnsavedSource', () => {
    const { bar, status } = setup();
    const a = status.forBook('book-a');
    const b = status.forBook('book-b');
    const unsavedA = a.store('kb:e1');
    unsavedA.set('offline');
    expect(bar.getState()).toBe('offline');

    status.activate('book-b');
    expect(bar.getState()).toBe('saved');
    b.store('write:s1').set('saving');
    expect(bar.getState()).toBe('saving');

    // The old book's controller goes on trying and reports; the dot stays with the open book.
    unsavedA.set('saving');
    unsavedA.set('saved');
    expect(bar.getState()).toBe('saving');

    status.activate('book-a');
    expect(bar.getState()).toBe('saved');
  });

  it('WhenASourceIsReleased_ShouldIgnoreWhatItsControllerReportsAfterwards', () => {
    const { bar, book } = setup();
    const store = book.store('plan:s1:title');
    store.set('unsaved');
    book.release('plan:s1:title', store);

    store.set('retry');

    expect(bar.getState()).toBe('saved');
  });

  it('Release_WithAStoreThatIsNoLongerTheRegisteredOne_ShouldLeaveTheCurrentSourceAlone', () => {
    const { bar, book } = setup();
    const old = book.store('write:s1');
    book.release('write:s1', old);
    const current = book.store('write:s1');
    current.set('offline');

    // The screen that left tries to release by key after another one took the source over.
    book.release('write:s1', old);

    expect(bar.getState()).toBe('offline');
  });

  it('ShouldSumTheWaitingEditsOfTheOpenBook', () => {
    const { bar, book } = setup();
    book.store('kb:e1').setWaiting(1);
    book.store('kb:e2').setWaiting(2);
    expect(bar.getWaiting()).toBe(3);
  });

  it('Reset_ShouldForgetEverySourceAndClearTheDot', () => {
    const { bar, status, book } = setup();
    book.store('kb:e1').set('retry');

    status.reset();

    expect(bar.getState()).toBe('saved');
    expect(book.store('kb:e1').getState()).toBe('saved');
  });
});
