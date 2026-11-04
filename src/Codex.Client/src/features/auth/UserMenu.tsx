import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { logout } from '@codex/core/api/auth';
import { useMe } from '../../api/authQueries';
import { shellTestIds } from '../../shell/shellTestIds';
import { accountLink, aiSettingsLink, usersLink } from './links';
import { authTestIds } from './testIds';

// The mockup's `.avatar` at the end of every bar: the signed-in user's initials in a 22px accent-soft circle, opening
// Account, AI, Users (admins only) and Sign out. Nothing renders until /api/auth/me has answered.
export function UserMenu() {
  const me = useMe();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return undefined;
    const close = (event: MouseEvent | KeyboardEvent) => {
      if (
        event instanceof KeyboardEvent
          ? event.key === 'Escape'
          : !root.current?.contains(event.target as Node)
      ) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', close);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', close);
    };
  }, [open]);

  if (me.data === undefined) return null;
  const user = me.data;

  async function signOut() {
    await logout();
    window.location.assign('/login');
  }

  const item =
    'block w-full border-0 bg-transparent px-3 py-1.5 text-left text-sm text-text no-underline hover:bg-accent-soft';
  return (
    <div ref={root} className="relative flex-none">
      <button
        type="button"
        data-testid={shellTestIds.avatar}
        aria-label={`Account menu for ${user.displayName}`}
        aria-haspopup="menu"
        aria-expanded={open}
        title={user.displayName}
        onClick={() => setOpen((value) => !value)}
        className="grid size-[22px] flex-none cursor-pointer place-items-center rounded-full border-0 bg-accent-soft p-0 text-[10px] font-bold text-accent"
      >
        {user.initials}
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 top-full z-20 mt-1 w-56 rounded border border-border bg-surface py-1 shadow-lg"
        >
          <div className="border-b border-border px-3 pb-2 pt-1">
            <div
              data-testid={authTestIds.userMenuName}
              className="truncate text-sm font-medium text-text"
            >
              {user.displayName}
            </div>
            <div className="truncate text-xs text-muted">{user.email}</div>
          </div>
          <Link
            role="menuitem"
            to={accountLink()}
            data-testid={authTestIds.userMenuAccount}
            className={item}
            onClick={() => setOpen(false)}
          >
            Account
          </Link>
          <Link
            role="menuitem"
            to={aiSettingsLink()}
            data-testid={authTestIds.userMenuAi}
            className={item}
            onClick={() => setOpen(false)}
          >
            AI
          </Link>
          {user.isAdmin && (
            <Link
              role="menuitem"
              to={usersLink()}
              data-testid={authTestIds.userMenuUsers}
              className={item}
              onClick={() => setOpen(false)}
            >
              Users
            </Link>
          )}
          <button
            role="menuitem"
            type="button"
            data-testid={authTestIds.userMenuSignOut}
            className={item}
            onClick={signOut}
          >
            Sign out
          </button>
        </div>
      )}
    </div>
  );
}
