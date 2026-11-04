/** A new array with the item at `from` moved by `delta` places; the same array when the move falls off either end. */
export function moveBy<T>(items: readonly T[], from: number, delta: number): readonly T[] {
  const to = from + delta;
  if (delta === 0 || from < 0 || from >= items.length || to < 0 || to >= items.length) return items;
  const next = [...items];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

export function sameOrder(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((id, i) => id === b[i]);
}

/** "001": the series number as the overline and the order list show it. */
export const paddedNumber = (n: number) => String(n).padStart(3, '0');
