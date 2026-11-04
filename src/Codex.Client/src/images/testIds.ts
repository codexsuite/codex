// data-testid registry for the shared image picker and its dialog frame (prefix images-), used by Write's figures and
// Plan's opener art.
export const imagesTestIds = {
  picker: 'images-picker',
  option: (assetId: string) => `images-option-${assetId}`,
  optionStatus: (assetId: string) => `images-option-status-${assetId}`,
  optionDpi: (assetId: string) => `images-option-dpi-${assetId}`,
  empty: 'images-empty',
  loadError: 'images-load-error',
  upload: 'images-upload',
  uploadInput: 'images-upload-input',
  uploadError: 'images-upload-error',
  selected: 'images-selected',
  selectedDpi: 'images-selected-dpi',
  dialogClose: 'images-dialog-close',
  dialogCancel: 'images-dialog-cancel',
} as const;
