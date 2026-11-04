// The mobile editor page's data-testid registry (prefix mobile-editor-).
export const mobileEditorTestIds = {
  root: 'mobile-editor-root',
  scene: (sceneId: string) => `mobile-editor-scene-${sceneId}`,
  sceneTitle: (sceneId: string) => `mobile-editor-scene-title-${sceneId}`,
  sceneProse: (sceneId: string) => `mobile-editor-scene-prose-${sceneId}`,
  sceneEditor: (sceneId: string) => `mobile-editor-scene-editor-${sceneId}`,
  sceneCard: (sceneId: string) => `mobile-editor-scene-card-${sceneId}`,
  sceneNumber: (sceneId: string) => `mobile-editor-scene-number-${sceneId}`,
  sceneWords: (sceneId: string) => `mobile-editor-scene-words-${sceneId}`,
  sceneSummary: (sceneId: string) => `mobile-editor-scene-summary-${sceneId}`,
  sceneLabel: (sceneId: string, label: string) => `mobile-editor-scene-label-${sceneId}-${label}`,
  sceneChip: (sceneId: string, entryId: string) => `mobile-editor-scene-chip-${sceneId}-${entryId}`,
  beat: (beatId: string) => `mobile-editor-beat-${beatId}`,
  beatInstruction: (beatId: string) => `mobile-editor-beat-instruction-${beatId}`,
  beatHidden: (beatId: string) => `mobile-editor-beat-hidden-${beatId}`,
  beatLength: (beatId: string) => `mobile-editor-beat-length-${beatId}`,
  addition: (additionId: string) => `mobile-editor-addition-${additionId}`,
} as const;
