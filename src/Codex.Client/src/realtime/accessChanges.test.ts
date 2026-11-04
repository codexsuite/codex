import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import { meKey } from '@codex/core/api/auth';
import { bookKey } from '@codex/core/api/books';
import type { BookHeader } from '@codex/core/api/types';
import { applyAccessChange } from './accessChanges';

const bookId = '0199a000-0000-7000-8000-000000000012';
const me = '0199c0de-0000-7000-8000-000100000001';

function setup(role: string) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(meKey, { id: me });
  queryClient.setQueryData(bookKey(bookId), {
    id: bookId,
    title: 'Glass Orchard',
    role,
    owner: { id: 'o', displayName: 'Mara Lind' },
  } as unknown as BookHeader);
  const toast = vi.fn();
  const leave = vi.fn();
  const apply = (next: 'view' | 'comment' | 'write' | null, userId = me) =>
    applyAccessChange(
      { type: 'accessChanged', bookId, userId, role: next },
      { queryClient, toast, leave },
    );
  const role$ = () => queryClient.getQueryData<BookHeader>(bookKey(bookId))?.role;
  return { apply, toast, leave, role: role$ };
}

describe('applyAccessChange', () => {
  it('puts a lower role in the header at once and says the tab is read-only', () => {
    const { apply, toast, role } = setup('write');
    apply('comment');
    expect(role()).toBe('comment');
    expect(toast).toHaveBeenCalledWith({
      message: 'Mara Lind changed your access to Can comment. This tab is read-only now.',
    });
  });

  it('says typing works again when a reader becomes an editor', () => {
    const { apply, toast, role } = setup('view');
    apply('write');
    expect(role()).toBe('write');
    expect(toast).toHaveBeenCalledWith({
      message: 'You can edit this book now. Typing works again.',
    });
  });

  it('leaves for the Library when access is removed', () => {
    const { apply, toast, leave } = setup('write');
    apply(null);
    expect(leave).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith({
      message: 'Mara Lind stopped sharing “Glass Orchard” with you.',
    });
  });

  it('ignores an event about another user', () => {
    const { apply, toast, leave, role } = setup('write');
    apply(null, '0199c0de-0000-7000-8000-0000000000ff');
    expect(leave).not.toHaveBeenCalled();
    expect(toast).not.toHaveBeenCalled();
    expect(role()).toBe('write');
  });
});
