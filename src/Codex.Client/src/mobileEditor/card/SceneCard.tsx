import type { BridgeScene } from '@codex/core/editing/editorBridge';
import { sceneLabel, wordsLabel } from '../format';
import { mobileEditorTestIds } from '../testIds';
import { KbChip } from './KbChip';
import { LabelPill } from './LabelPill';

interface SceneCardProps {
  scene: BridgeScene;
  /** The scene being edited shows its summary and chips; any other collapses to one line. */
  open: boolean;
  onOpen: (sceneId: string) => void;
}

// The card above a scene's prose (design/screens/mobile.html `.gut`): `Scene n`, title, labels and words, then the summary
// and Knowledge base chips. Tapping it asks the app to open the scene sheet.
export function SceneCard({ scene, open, onOpen }: SceneCardProps) {
  return (
    <button
      type="button"
      className={open ? 'me-card' : 'me-card me-card-collapsed'}
      data-testid={mobileEditorTestIds.sceneCard(scene.id)}
      data-open={open}
      onClick={() => onOpen(scene.id)}
    >
      <span className="me-card-head">
        <b data-testid={mobileEditorTestIds.sceneNumber(scene.id)}>{sceneLabel(scene.number)}</b>
        <span
          className={scene.title === '' ? 'me-card-title me-untitled' : 'me-card-title'}
          data-testid={mobileEditorTestIds.sceneTitle(scene.id)}
        >
          {scene.title === '' ? 'Untitled' : scene.title}
        </span>
        {open &&
          scene.labels.map((label) => <LabelPill key={label} sceneId={scene.id} label={label} />)}
        <span className="me-card-words" data-testid={mobileEditorTestIds.sceneWords(scene.id)}>
          {wordsLabel(scene.words)}
        </span>
      </span>
      {open && scene.summary !== '' && (
        <span className="me-card-summary" data-testid={mobileEditorTestIds.sceneSummary(scene.id)}>
          {scene.summary}
        </span>
      )}
      {open && scene.chips.length > 0 && (
        <span className="me-card-chips">
          {scene.chips.map((chip) => (
            <KbChip key={chip.id} sceneId={scene.id} chip={chip} />
          ))}
        </span>
      )}
    </button>
  );
}
