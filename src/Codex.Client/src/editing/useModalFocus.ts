import { useEffect, useRef, type RefObject } from 'react';

const focusable =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"]), [contenteditable="true"]';

// Where each modal sent focus on its way out, so a modal that replaces another (a loading frame swapped for the loaded
// one) can still return to the control that opened the first.
const returnTargets = new WeakMap<Element, HTMLElement>();

/**
 * Focus for a modal that is not a native `<dialog>` (which does all three itself): focus moves inside when it opens
 * (the container, unless something inside already has it), Tab and Shift+Tab wrap at its ends, and focus goes back to
 * whatever had it when the modal goes away. The container needs `tabIndex={-1}`.
 */
export function useModalFocus(container: RefObject<HTMLElement | null>) {
  // Read while rendering, before any effect (a child's, or the caller's) moves focus.
  const opener = useRef(document.activeElement);
  const replaced = useRef(
    document.activeElement?.closest('[role="dialog"], [role="alertdialog"]') ?? null,
  );
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const from = opener.current;
    const via = replaced.current;
    if (!element.contains(document.activeElement)) element.focus();

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || event.defaultPrevented) return;
      // A dialog inside this one traps its own Tab.
      const owner = (event.target as Element).closest(
        'dialog, [role="dialog"], [role="alertdialog"]',
      );
      if (owner !== null && owner !== element) return;
      const stops = [...element.querySelectorAll<HTMLElement>(focusable)].filter(
        (stop) => stop.getClientRects().length > 0,
      );
      const first = stops[0];
      const last = stops[stops.length - 1];
      if (!first || !last) {
        event.preventDefault();
        return;
      }
      const active = document.activeElement;
      if (event.shiftKey && (active === first || active === element)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || active === element)) {
        event.preventDefault();
        first.focus();
      }
    };
    element.addEventListener('keydown', onKey);
    return () => {
      element.removeEventListener('keydown', onKey);
      let target = from instanceof HTMLElement && from.isConnected ? from : null;
      if (!target && via) target = returnTargets.get(via) ?? null;
      if (target?.isConnected) {
        returnTargets.set(element, target);
        target.focus();
      }
    };
  }, [container]);
}
