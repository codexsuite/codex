import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { sharingApi, sharingKey, type GrantRole } from '@codex/core/api/sharing';
import type { Sharing, SharingGrant, SharingUser } from '@codex/core/api/types';
import { showToast } from '@codex/core/editing/toastStore';
import {
  addedToast,
  changedToast,
  grantOn,
  removedToast,
  type GrantScope,
} from '@codex/core/features/settings/sharingRules';

export interface SharingChanges {
  add: (scope: GrantScope, user: SharingUser, role: GrantRole) => Promise<boolean>;
  change: (scope: GrantScope, user: SharingUser, role: GrantRole) => Promise<void>;
  remove: (scope: GrantScope, user: SharingUser) => Promise<void>;
  error: string | null;
}

function withGrant(
  sharing: Sharing,
  scope: GrantScope,
  user: SharingUser,
  role: GrantRole | null,
): Sharing {
  const put = (list: SharingGrant[]) => {
    const others = list.filter((g) => g.user.id !== user.id);
    if (role === null) return others;
    const at = list.findIndex((g) => g.user.id === user.id);
    const next = { user, role };
    return at < 0 ? [...list, next] : list.map((g) => (g.user.id === user.id ? next : g));
  };
  if (scope === 'book') return { ...sharing, book: put(sharing.book) };
  return sharing.series
    ? { ...sharing, series: { ...sharing.series, grants: put(sharing.series.grants) } }
    : sharing;
}

/**
 * Every change saves straight away (`PUT`/`DELETE …/sharing/{userId}` on the book, or on the series for the
 * whole-series list), shows at once, and offers Undo in its toast. Every book's card reads its series' grants, so all
 * sharing queries refetch afterwards.
 */
export function useSharingChanges(bookId: string, sharing: Sharing, title: string): SharingChanges {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const key = sharingKey(bookId);
  const target = (scope: GrantScope) =>
    scope === 'series' && sharing.series ? sharing.series.id : bookId;

  const apply = async (scope: GrantScope, user: SharingUser, role: GrantRole | null) => {
    setError(null);
    const current = queryClient.getQueryData<Sharing>(key);
    if (current) queryClient.setQueryData(key, withGrant(current, scope, user, role));
    try {
      if (role === null) await sharingApi.removeGrant(target(scope), user.id);
      else await sharingApi.setGrant(target(scope), user.id, role);
      return true;
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The change could not be saved.');
      return false;
    } finally {
      await queryClient.invalidateQueries({ queryKey: ['sharing'] });
    }
  };

  const undoTo = (scope: GrantScope, user: SharingUser, role: GrantRole | null) => async () => {
    await apply(scope, user, role);
  };

  return {
    error,
    add: async (scope, user, role) => {
      const saved = await apply(scope, user, role);
      if (saved) {
        showToast({
          message: addedToast(sharing, scope, title, user.displayName, role),
          undo: undoTo(scope, user, null),
        });
      }
      return saved;
    },
    change: async (scope, user, role) => {
      const before = grantOn(sharing, scope, user.id);
      if (await apply(scope, user, role)) {
        showToast({
          message: changedToast(sharing, scope, title, user.displayName, role),
          undo: undoTo(scope, user, before),
        });
      }
    },
    remove: async (scope, user) => {
      const before = grantOn(sharing, scope, user.id);
      if (await apply(scope, user, null)) {
        showToast({
          message: removedToast(sharing, scope, user),
          undo: undoTo(scope, user, before),
        });
      }
    },
  };
}
