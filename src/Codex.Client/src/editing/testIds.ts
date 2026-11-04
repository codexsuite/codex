// data-testid registry for the shared editing utilities (prefix editing-).
export const editingTestIds = {
  saveStatus: 'editing-save-status',
  saveStatusReload: 'editing-save-status-reload',
  saveStatusRetry: 'editing-save-status-retry',
  labelPicker: 'editing-label-picker',
  labelOption: (labelId: string) => `editing-label-option-${labelId}`,
  labelCreate: 'editing-label-create',
  toastRegion: 'editing-toast-region',
  toast: 'editing-toast',
  toastUndo: 'editing-toast-undo',
  toastDismiss: 'editing-toast-dismiss',
} as const;
