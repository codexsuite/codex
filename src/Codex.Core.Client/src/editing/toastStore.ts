export const TOAST_TIMEOUT_MS = 6000;

export interface ToastRequest {
  message: string;
  // When given, the toast shows an Undo button. The toast closes once it resolves.
  undo?: () => Promise<void>;
}

export interface ToastItem extends ToastRequest {
  id: number;
}

let items: ToastItem[] = [];
let nextId = 1;
const listeners = new Set<() => void>();

function publish(next: ToastItem[]) {
  items = next;
  listeners.forEach((listener) => listener());
}

// Callable from anywhere; the app's toast host (web: <ToastHost />) renders the result.
export function showToast(request: ToastRequest): number {
  const id = nextId++;
  publish([...items, { ...request, id }].slice(-3));
  return id;
}

export function dismissToast(id: number) {
  publish(items.filter((item) => item.id !== id));
}

export function subscribeToasts(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export const getToasts = (): ToastItem[] => items;
