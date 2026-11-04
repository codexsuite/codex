import type { ReactNode } from 'react';

interface FieldRowProps {
  label: string;
  htmlFor?: string;
  help?: ReactNode;
  /** After the label: the Series / This book × chip. */
  chip?: ReactNode;
  children?: ReactNode;
}

// Label on the left (168px, as tall as a control so it centres on one), the control, then help text under it.
// Dashed rule between rows; the label goes above the control below 640px.
export function FieldRow({ label, htmlFor, help, chip, children }: FieldRowProps) {
  return (
    <div className="grid grid-cols-1 items-start gap-x-3 gap-y-0.5 border-t border-dashed border-border py-[7px] first:border-t-0 min-[640px]:grid-cols-[168px_minmax(0,1fr)] min-[640px]:gap-y-1">
      <label
        htmlFor={htmlFor}
        className="flex flex-wrap content-center items-center gap-x-1.5 gap-y-0.5 text-sm text-muted min-[640px]:min-h-[var(--control-height)]"
      >
        {label}
        {chip}
      </label>
      <div className="flex min-h-[var(--control-height)] min-w-0 flex-wrap items-center gap-x-2 gap-y-1.5">
        {children}
        {help && <p className="m-0 basis-full text-xs leading-[1.45] text-muted">{help}</p>}
      </div>
    </div>
  );
}
