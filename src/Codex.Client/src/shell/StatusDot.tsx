// Book status: a 6px dot in the status colour plus the label (design/README.md -> Rules).
const colours: Record<string, string> = {
  planning: 'var(--status-planning)',
  draft: 'var(--status-draft)',
  writing: 'var(--status-writing)',
  revising: 'var(--status-revising)',
  published: 'var(--status-published)',
};

export function StatusDot({ status }: { status: string }) {
  return (
    <span
      className="inline-flex items-center gap-1 text-2xs font-medium capitalize"
      style={{ color: colours[status] ?? 'var(--color-muted)' }}
    >
      <i aria-hidden="true" className="size-[6px] rounded-full bg-current" />
      {status}
    </span>
  );
}
