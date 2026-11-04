import { useSyncExternalStore } from 'react';
import { getToasts, subscribeToasts, type ToastItem } from '@codex/core/editing/toastStore';

export function useToasts(): ToastItem[] {
  return useSyncExternalStore(subscribeToasts, getToasts);
}
