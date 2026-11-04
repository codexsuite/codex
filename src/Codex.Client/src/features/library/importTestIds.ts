// Import lane ids (prefix import-): the dialog's step 2 and the Plan banner. Step 1's ids are the library lane's.
// Mirrored in Codex.AcceptanceTests/Pages/Import/ImportTestIds.cs.
export const importTestIds = {
  preview: 'import-preview',
  previewTitle: 'import-preview-title',
  /** By the row's item, lower-cased with dashes: "Scene beats" -> import-preview-row-scene-beats. */
  previewRow: (item: string) =>
    `import-preview-row-${item
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '')}`,
  skipped: 'import-skipped',
  skippedRow: 'import-skipped-row',
  previewError: 'import-preview-error',
  selectionError: 'import-selection-error',
  runError: 'import-run-error',
  loading: 'import-loading',
  run: 'import-run',
  banner: 'import-banner',
  bannerText: 'import-banner-text',
  bannerSkipped: 'import-banner-skipped',
  bannerSkippedRow: 'import-banner-skipped-row',
  undoConfirm: 'import-undo-confirm',
  undoConfirmDelete: 'import-undo-confirm-delete',
  undoConfirmCancel: 'import-undo-confirm-cancel',
  undo: 'import-undo',
  dismiss: 'import-dismiss',
  bannerError: 'import-banner-error',
} as const;
