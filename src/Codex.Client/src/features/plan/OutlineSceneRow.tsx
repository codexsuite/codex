import type { OutlineScene } from '@codex/core/api/structure';
import { EntryChip } from './EntryChip';
import type { Emphasis } from '@codex/core/features/plan/emphasis';
import { plainSummary } from '@codex/core/features/plan/format';
import { LabelPill } from './LabelPill';
import { planTestIds } from './testIds';

export function OutlineSceneRow({ scene, emphasis }: { scene: OutlineScene; emphasis: Emphasis }) {
  const summary = plainSummary(scene.summary);
  return (
    <tr
      id={`scene-${scene.id}`}
      data-testid={planTestIds.outlineScene(scene.id)}
      data-emphasis={emphasis}
      className={`border-t border-border ${emphasis === 'dim' ? 'opacity-40' : ''} ${emphasis === 'hit' ? 'bg-accent-soft' : ''}`}
    >
      <td data-testid={planTestIds.outlineSceneNumber} className="px-2 py-1 text-right text-muted">
        {scene.number}
      </td>
      <td className="max-w-0 truncate px-2 py-1">
        {summary === '' ? <span className="text-muted">Empty scene</span> : summary}
      </td>
      <td className="px-2 py-1">
        <div className="flex flex-wrap gap-1">
          {scene.linkedEntries.map((entry) => (
            <EntryChip key={entry.id} sceneId={scene.id} entry={entry} />
          ))}
        </div>
      </td>
      <td className="px-2 py-1">
        <div className="flex flex-wrap gap-1">
          {scene.labels.map((label) => (
            <LabelPill key={label.id} sceneId={scene.id} label={label} />
          ))}
        </div>
      </td>
      <td className={`px-2 py-1 text-right ${scene.words > 0 ? '' : 'text-muted'}`}>
        {scene.words > 0 ? scene.words.toLocaleString('en-US') : '—'}
      </td>
    </tr>
  );
}
