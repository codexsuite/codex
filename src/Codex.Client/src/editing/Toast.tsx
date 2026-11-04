import { useEffect, useRef, useState } from 'react';
import { editingTestIds } from './testIds';
import { dismissToast, TOAST_TIMEOUT_MS, type ToastItem } from '@codex/core/editing/toastStore';
import { useToasts } from './useToasts';

// Renders the toasts. Mount once near the app root. Toasts never take focus; the region is a polite live region
// and the timer pauses while the pointer or keyboard focus is inside a toast.
export function ToastHost() {
  const toasts = useToasts();
  return (
    <div
      data-testid={editingTestIds.toastRegion}
      role="region"
      aria-label="Notifications"
      className="pointer-events-none fixed bottom-4 left-1/2 z-50 flex -translate-x-1/2 flex-col gap-2"
    >
      {toasts.map((toast) => (
        <Toast key={toast.id} toast={toast} />
      ))}
    </div>
  );
}

function Toast({ toast }: { toast: ToastItem }) {
  const [paused, setPaused] = useState(false);
  const [failed, setFailed] = useState(false);
  const [working, setWorking] = useState(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    if (paused || working) return;
    const timer = setTimeout(() => dismissToast(toast.id), TOAST_TIMEOUT_MS);
    return () => clearTimeout(timer);
  }, [paused, working, toast.id]);

  const undo = async () => {
    if (!toast.undo) return;
    setWorking(true);
    try {
      await toast.undo();
      dismissToast(toast.id);
    } catch {
      if (mounted.current) {
        setFailed(true);
        setWorking(false);
      }
    }
  };

  return (
    <div
      data-testid={editingTestIds.toast}
      role="status"
      className="pointer-events-auto flex items-center gap-3 rounded-md border border-border bg-surface px-3 py-2 text-sm text-text shadow-raised"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
    >
      <span>{failed ? 'Could not undo.' : toast.message}</span>
      {toast.undo && !failed ? (
        <button
          type="button"
          data-testid={editingTestIds.toastUndo}
          disabled={working}
          className="font-semibold text-accent underline"
          onClick={() => void undo()}
        >
          Undo
        </button>
      ) : null}
      <button
        type="button"
        data-testid={editingTestIds.toastDismiss}
        aria-label="Dismiss"
        className="text-muted"
        onClick={() => dismissToast(toast.id)}
      >
        ×
      </button>
    </div>
  );
}
