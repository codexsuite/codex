import { useState } from 'react';
import { AppRoutes } from '../AppRoutes';
import { showToast } from '@codex/core/editing/toastStore';
import { shellToastHarnessIds } from './shellToastHarnessIds';

// The real app routes (and so the real Shell) plus a button that calls showToast the way a lane page would.
export function ShellToastHarness() {
  const [undos, setUndos] = useState(0);
  return (
    <>
      <AppRoutes />
      <button
        type="button"
        data-testid={shellToastHarnessIds.show}
        onClick={() =>
          showToast({
            message: 'Archived 1 scene',
            undo: async () => setUndos((n) => n + 1),
          })
        }
      >
        show
      </button>
      <output data-testid={shellToastHarnessIds.undos}>{undos}</output>
    </>
  );
}
