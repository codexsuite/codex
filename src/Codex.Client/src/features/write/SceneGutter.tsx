import { useState, type CSSProperties } from 'react';
import { useParams } from 'react-router-dom';
import { useCanWrite } from '../../shell/bookAccess';
import { ActionsMenu } from './ActionsMenu';
import { useKbInfo } from './kbInfo';
import { LabelChip } from './LabelChip';
import type { SceneView } from '@codex/core/features/write/manuscriptView';
import { SceneEntryAdd } from './SceneEntryAdd';
import { SceneLabelPicker } from './SceneLabelPicker';
import { SceneSummary } from './SceneSummary';
import { writeTestIds } from './testIds';
import { VersionHistory } from './VersionHistory';

interface SceneGutterProps {
  view: SceneView;
}

// The column beside a scene's prose: where it is, its labels, the summary shared with Plan, the Codex entries
// linked to it, then Label, + Codex and its word count. Sticky, so it stays in view beside a long scene.
export function SceneGutter({ view }: SceneGutterProps) {
  const { bookId = '' } = useParams();
  const infos = useKbInfo(bookId);
  const { scene } = view;
  const [historyOpen, setHistoryOpen] = useState(false);
  const canWrite = useCanWrite();
  return (
    <aside aria-label={view.heading} className="write-gutter min-w-0 text-sm">
      <div className="sticky top-3 pt-0.5">
        <div
          data-testid={writeTestIds.sceneHeading(scene.id)}
          className="write-overline flex min-h-[22px] items-center truncate"
        >
          <span className="flex-1 truncate">{view.heading}</span>
          <span className="write-scene-more">
            <ActionsMenu
              label="Scene actions"
              buttonTestId={writeTestIds.sceneMore(scene.id)}
              menuTestId={writeTestIds.sceneMenu(scene.id)}
            >
              {(close) => (
                <button
                  type="button"
                  role="menuitem"
                  data-testid={writeTestIds.sceneHistory(scene.id)}
                  className="write-menu-item"
                  onClick={() => {
                    close();
                    setHistoryOpen(true);
                  }}
                >
                  Version history
                </button>
              )}
            </ActionsMenu>
          </span>
        </div>
        {historyOpen && (
          <VersionHistory
            sceneId={scene.id}
            heading={view.heading}
            onClose={() => setHistoryOpen(false)}
          />
        )}
        {scene.labels.length > 0 && (
          <div className="mt-0.5 mb-1 flex flex-wrap gap-1">
            {scene.labels.map((label) => (
              <LabelChip key={label.id} label={label} testId={writeTestIds.sceneLabel(scene.id)} />
            ))}
          </div>
        )}
        <div data-testid={writeTestIds.sceneSummary(scene.id)} className="write-summary">
          <SceneSummary sceneId={scene.id} summary={scene.summary} />
        </div>
        {scene.linkedEntries.length > 0 && (
          <div className="mt-1.5 flex flex-wrap gap-1">
            {scene.linkedEntries.map((entry) => (
              <span
                key={entry.id}
                data-testid={writeTestIds.sceneEntry(scene.id)}
                data-entry-id={entry.id}
                className="write-cx"
                style={{ '--cat': infos.get(entry.id)?.colour } as CSSProperties}
              >
                <i />
                {entry.name}
              </span>
            ))}
          </div>
        )}
        <div className="-ml-1.5 mt-1 flex flex-wrap items-center gap-0.5">
          {canWrite && (
            <>
              <SceneLabelPicker sceneId={scene.id} labels={scene.labels} />
              <SceneEntryAdd sceneId={scene.id} linked={scene.linkedEntries} />
            </>
          )}
          <span className="flex-1" />
          <span
            data-testid={writeTestIds.sceneWords(scene.id)}
            className={`write-words ${scene.words === 0 ? 'italic' : ''}`}
          >
            {view.wordsLabel}
          </span>
        </div>
      </div>
    </aside>
  );
}
