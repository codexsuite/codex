import type { ReactNode } from 'react';

// A titled block of the panel with an optional note and action on its heading line.
export function PanelSection({
  title,
  note,
  action,
  children,
}: {
  title: string;
  note?: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="border-b border-border p-3">
      <div className="mb-2 flex items-center gap-1.5">
        <h3 className="m-0 flex-1 text-sm font-semibold text-text">{title}</h3>
        {note && <span className="text-xs text-muted tabular-nums">{note}</span>}
        {action}
      </div>
      {children}
    </section>
  );
}
