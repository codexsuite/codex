import { SlotFill } from '../../shell/Slots';
import { ChevronDownIcon } from './icons';
import type { ActView } from '@codex/core/features/write/manuscriptView';
import {
  countsLabel,
  parseScope,
  scopeCounts,
  scopeValue,
  type Scope,
} from '@codex/core/features/write/scope';
import { writeTestIds } from './testIds';

interface ScopeSelectorProps {
  /** Every act of the book, not just the ones in scope. */
  acts: readonly ActView[];
  scope: Scope;
  /** What is in scope now; its counts are drawn beside the picker. */
  inScope: readonly ActView[];
  onChange: (scope: Scope) => void;
}

// The book bar's scope picker (Book / Act / Chapter / Scene) and the word, page and read-time counts for what it
// narrows to.
export function ScopeSelector({ acts, scope, inScope, onChange }: ScopeSelectorProps) {
  return (
    <SlotFill slot="scope">
      <div className="ml-2 flex min-w-0 items-center gap-2">
        <span className="write-sel max-w-[240px]">
          <em>Scope</em>
          <select
            data-testid={writeTestIds.scopeSelect}
            aria-label="Scope"
            value={scopeValue(scope)}
            onChange={(event) => onChange(parseScope(event.target.value))}
          >
            <option value="book">Full manuscript</option>
            <optgroup label="Act">
              {acts.map((view) => (
                <option key={view.act.id} value={`act:${view.act.id}`}>
                  {view.marker}
                </option>
              ))}
            </optgroup>
            <optgroup label="Chapter">
              {acts
                .flatMap((view) => view.chapters)
                .map(({ chapter, overline }) => (
                  <option key={chapter.id} value={`chapter:${chapter.id}`}>
                    {overline ? `${overline}: ${chapter.title}` : chapter.title}
                  </option>
                ))}
            </optgroup>
            <optgroup label="Scene">
              {acts
                .flatMap((view) => view.chapters)
                .flatMap((chapter) => chapter.scenes)
                .map(({ scene, heading }) => (
                  <option key={scene.id} value={`scene:${scene.id}`}>
                    {heading}
                  </option>
                ))}
            </optgroup>
          </select>
          <ChevronDownIcon />
        </span>
        <span
          data-testid={writeTestIds.scopeCounts}
          className="text-xs whitespace-nowrap text-muted tabular-nums"
        >
          {countsLabel(scopeCounts(inScope))}
        </span>
      </div>
    </SlotFill>
  );
}
