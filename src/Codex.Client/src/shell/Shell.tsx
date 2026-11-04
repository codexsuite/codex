import { Outlet } from 'react-router-dom';
import { ToastHost } from '../editing/Toast';
import { testIds } from '../testIds';
import { PaletteProvider } from './CommandPalette';
import { SlotsProvider } from './Slots';

// The root every page renders inside: slot registry, the ⌘K palette and its shortcut, the routed page, and the one
// toast host (any lane calls showToast from src/editing/toastStore).
// Each page draws its own bar (Library topbar, book bar), as in the reference screens.
export function Shell() {
  return (
    <SlotsProvider>
      <PaletteProvider>
        <div data-testid={testIds.shell.appShell} className="min-h-screen bg-bg text-text">
          <Outlet />
          <ToastHost />
        </div>
      </PaletteProvider>
    </SlotsProvider>
  );
}
