import { refsTestIds } from './testIds';

export function SourceGroupRow({
  name,
  count,
  dot,
  span,
}: {
  name: string;
  count: number;
  dot?: string;
  span: number;
}) {
  return (
    <tr data-testid={refsTestIds.group(name)}>
      <td
        colSpan={span}
        className="h-[26px] border-b border-border bg-bg px-2 text-2xs font-semibold tracking-[.04em] text-muted uppercase"
      >
        {dot && (
          <i
            className={`mr-1.5 inline-block size-1.5 rounded-full align-[1px] ${dot}`}
            aria-hidden
          />
        )}
        {name} · {count}
      </td>
    </tr>
  );
}
