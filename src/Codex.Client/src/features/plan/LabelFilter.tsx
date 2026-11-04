import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { structureApi, type Outline } from '@codex/core/api/structure';
import { toneOf } from '../../editing/labelTone';
import { ChevronDownIcon } from './icons';
import { LabelManager } from './LabelManager';
import { planTestIds } from './testIds';
import { ToolbarPopover } from './ToolbarPopover';
import { toolbarButton } from './toolbarStyles';
import { useCanWrite } from '../../shell/bookAccess';

const dot = { ok: 'bg-ok', info: 'bg-info', warn: 'bg-warn', danger: 'bg-danger' } as const;

const itemClass =
  'flex min-h-7 w-full cursor-pointer items-center gap-[7px] rounded-sm border-0 bg-transparent px-2 text-left text-sm text-text hover:bg-accent-soft aria-checked:bg-accent-soft';

// `Label Any ▾`: dims the scenes that do not carry the picked label. The panel lists the book's (and series') labels
// with how many live scenes carry each.
export function LabelFilter({
  outline,
  labelId,
  onChange,
}: {
  outline: Outline | undefined;
  labelId: string | null;
  onChange: (labelId: string | null) => void;
}) {
  // Below write (#80) labels filter, but are not managed.
  const canWrite = useCanWrite();
  const { bookId = '' } = useParams();
  const [open, setOpen] = useState(false);
  const [managing, setManaging] = useState(false);
  const labels = useQuery({
    queryKey: ['labels', bookId],
    queryFn: ({ signal }) => structureApi.labels(bookId, { signal }),
  });
  const scenes = (outline?.acts ?? [])
    .flatMap((a) => a.chapters)
    .filter((c) => !c.archived)
    .flatMap((c) => c.scenes);
  const picked = labels.data?.find((l) => l.id === labelId);
  const pick = (id: string | null) => {
    onChange(id);
    setOpen(false);
  };
  return (
    <ToolbarPopover
      open={open}
      onClose={() => {
        setOpen(false);
        setManaging(false);
      }}
      testId={planTestIds.labelFilterPanel}
      trigger={
        <button
          type="button"
          data-testid={planTestIds.labelFilter}
          aria-haspopup="menu"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
          className={`${toolbarButton} !font-normal`}
        >
          <em className="text-muted not-italic">Label</em> <span>{picked?.name ?? 'Any'}</span>
          <span className="text-muted [&_svg]:!size-[11px]">
            <ChevronDownIcon />
          </span>
        </button>
      }
    >
      {managing ? (
        <LabelManager
          onDone={() => setManaging(false)}
          onDeleted={(id) => {
            if (id === labelId) onChange(null);
          }}
        />
      ) : (
        <div role="menu" className="overflow-auto p-1">
          <button
            type="button"
            role="menuitemradio"
            aria-checked={labelId === null}
            data-testid={planTestIds.labelOption('any')}
            onClick={() => pick(null)}
            className={itemClass}
          >
            Any label
            {labelId === null && <span className="ml-auto text-2xs text-muted">✓</span>}
          </button>
          {labels.data && labels.data.length > 0 && <div className="my-1 h-px bg-border" />}
          {labels.data?.map((label) => {
            const tone = toneOf(label.color);
            return (
              <button
                key={label.id}
                type="button"
                role="menuitemradio"
                aria-checked={labelId === label.id}
                data-testid={planTestIds.labelOption(label.id)}
                onClick={() => pick(label.id)}
                className={itemClass}
              >
                <span
                  aria-hidden="true"
                  className={`size-1.5 rounded-full ${tone === null ? 'border border-muted' : dot[tone]}`}
                />
                {label.name}
                <span className="ml-auto font-mono text-2xs text-muted">
                  {scenes.filter((s) => s.labels.some((l) => l.id === label.id)).length}
                </span>
              </button>
            );
          })}
          {canWrite && (
            <>
              <div className="my-1 h-px bg-border" />
              <button
                type="button"
                role="menuitem"
                data-testid={planTestIds.manageLabels}
                onClick={() => setManaging(true)}
                className={itemClass}
              >
                Manage labels…
              </button>
            </>
          )}
        </div>
      )}
    </ToolbarPopover>
  );
}
