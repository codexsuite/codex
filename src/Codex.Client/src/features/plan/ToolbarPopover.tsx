import { useEffect, useRef, type ReactNode } from 'react';

// A panel under its trigger (the mockup's `#pop`): `trigger` is the button, `children` the panel while `open`. A click
// outside or Esc closes it. The panel lines up with the trigger's right edge, or its left edge with `align="left"`.
export function ToolbarPopover({
  open,
  onClose,
  trigger,
  children,
  testId,
  wide = false,
  align = 'right',
}: {
  open: boolean;
  onClose: () => void;
  trigger: ReactNode;
  children: ReactNode;
  testId: string;
  wide?: boolean;
  align?: 'left' | 'right';
}) {
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return undefined;
    const dismiss = (event: Event) => {
      if (
        event instanceof KeyboardEvent
          ? event.key === 'Escape'
          : !root.current?.contains(event.target as Node)
      )
        onClose();
    };
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', dismiss);
    return () => {
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', dismiss);
    };
  }, [open, onClose]);
  return (
    <div ref={root} className="relative">
      {trigger}
      {open && (
        <div
          data-testid={testId}
          className={`absolute ${align === 'left' ? 'left-0' : 'right-0'} z-20 mt-1 flex max-h-80 flex-col overflow-hidden rounded-md border border-border bg-surface shadow-raised ${wide ? 'w-80 max-w-[calc(100vw-16px)]' : 'w-60'}`}
        >
          {children}
        </div>
      )}
    </div>
  );
}
