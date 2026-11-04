import type { OutlineAct } from '@codex/core/api/structure';
import { actName } from '@codex/core/features/plan/format';
import { planTestIds } from './testIds';

export function OutlineActRow({ act, index }: { act: OutlineAct; index: number }) {
  return (
    <tr data-testid={planTestIds.outlineAct(act.id)} className="bg-surface-2">
      <td colSpan={5} className="px-2 py-1 text-xs font-semibold text-text">
        {actName(index, act.title)}
      </td>
    </tr>
  );
}
