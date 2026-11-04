import { useEffect } from 'react';
import type { SaveCoordinator } from './saveCoordinator';

export const LEAVE_WARNING = 'You have changes that are not saved';

// Edits that are waiting for the 3 s timer or an in-flight save are sent when the editor leaves the screen, so only
// an edit that is stuck (offline, refused) is at risk. For that edit: closing or reloading the tab gets the browser's
// own "leave site?" prompt, and following a link inside the app asks first; declining keeps the editor as it was.
// (The router here is not a data router, so there is no blocker to use: links are caught as they are clicked.)
export function useLeaveGuard(coordinator: SaveCoordinator): void {
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (!coordinator.hasStuck()) return;
      event.preventDefault();
      // Some browsers show this text; the rest show their own.
      event.returnValue = LEAVE_WARNING;
    };
    const onClick = (event: MouseEvent) => {
      if (!coordinator.hasStuck() || event.defaultPrevented || event.button !== 0) return;
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const link = (event.target as Element | null)?.closest('a[href]');
      if (!link || link.getAttribute('target') === '_blank') return;
      const url = new URL(link.getAttribute('href') ?? '', window.location.href);
      if (url.origin !== window.location.origin) return;
      const here = window.location;
      // A link within the same screen (a hash) leaves nothing behind.
      if (url.pathname === here.pathname && url.search === here.search) return;
      if (!window.confirm(`${LEAVE_WARNING}. Leave anyway?`)) {
        event.preventDefault();
        event.stopPropagation();
      }
    };
    window.addEventListener('beforeunload', beforeUnload);
    // Capture phase: before React Router's own click handler navigates.
    document.addEventListener('click', onClick, true);
    return () => {
      window.removeEventListener('beforeunload', beforeUnload);
      document.removeEventListener('click', onClick, true);
    };
  }, [coordinator]);
}
