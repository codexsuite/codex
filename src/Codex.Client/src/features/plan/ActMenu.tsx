import { useState } from 'react';
import type { OutlineAct } from '@codex/core/api/structure';
import { MoreIcon } from './icons';
import { planTestIds } from './testIds';
import { ToolbarPopover } from './ToolbarPopover';
import { useActArchive } from './useActArchive';

const itemClass =
  'block w-full cursor-pointer rounded-sm border-0 bg-transparent px-2 py-1 text-left text-sm text-text hover:bg-accent-soft';

// The act's ⋯ (the end of its split button): Expand or Collapse act, Opener art…, then Delete act.
export function ActMenu({
  act,
  name,
  collapsed,
  onToggle,
  onOpener,
  className,
}: {
  act: OutlineAct;
  /** "Act 2: The Turn", for the button's label. */
  name: string;
  collapsed: boolean;
  onToggle: () => void;
  /** Opens Opener art… for the act. */
  onOpener: () => void;
  className: string;
}) {
  const [open, setOpen] = useState(false);
  const { archive } = useActArchive();
  return (
    <ToolbarPopover
      open={open}
      onClose={() => setOpen(false)}
      testId={planTestIds.actMenu(act.id)}
      align="left"
      trigger={
        <button
          type="button"
          data-testid={planTestIds.actMore(act.id)}
          aria-label={`${name} actions`}
          aria-haspopup="menu"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
          className={className}
        >
          <MoreIcon />
        </button>
      }
    >
      <div role="menu" className="p-1">
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.actMenuToggle(act.id)}
          onClick={() => {
            setOpen(false);
            onToggle();
          }}
          className={itemClass}
        >
          {collapsed ? 'Expand act' : 'Collapse act'}
        </button>
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.actOpener(act.id)}
          onClick={() => {
            setOpen(false);
            onOpener();
          }}
          className={itemClass}
        >
          Opener art…
        </button>
        <div className="my-1 h-px bg-border" />
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.actDelete(act.id)}
          onClick={() => {
            setOpen(false);
            void archive(act.id);
          }}
          className={`${itemClass} !text-danger`}
        >
          Delete act
        </button>
      </div>
    </ToolbarPopover>
  );
}
