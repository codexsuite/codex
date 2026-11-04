import { mobileEditorTestIds } from '../testIds';

export function LabelPill({ sceneId, label }: { sceneId: string; label: string }) {
  return (
    <span className="me-label" data-testid={mobileEditorTestIds.sceneLabel(sceneId, label)}>
      {label}
    </span>
  );
}
