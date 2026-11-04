// The Settings tab's test ids (prefix mobile-settings-). Row ids carry the field's name; the primitives' inner parts
// follow from them (`<row>-value`, `<row>-chip`).
export const settingsTestIds = {
  screen: 'mobile-settings-screen',
  loading: 'mobile-settings-loading',
  error: 'mobile-settings-error',
  readOnlyNote: 'mobile-settings-read-only-note',
  seriesFootnote: 'mobile-settings-series-footnote',
  row: (name: string) => `mobile-settings-row-${name}`,
  sheet: 'mobile-settings-sheet',
  sheetError: 'mobile-settings-sheet-error',
  field: 'mobile-settings-field',
  choice: (value: string) => `mobile-settings-choice-${value}`,
  choiceNote: 'mobile-settings-choice-note',
  styleGuideScreen: 'mobile-settings-style-guide-screen',
  styleGuideField: 'mobile-settings-style-guide-field',
  styleGuideError: 'mobile-settings-style-guide-error',
  styleGuideSeries: 'mobile-settings-style-guide-series',
  styleGuideWords: 'mobile-settings-style-guide-words',
  styleGuideNote: 'mobile-settings-style-guide-note',
  styleGuideStatus: 'mobile-settings-style-guide-status',
} as const;
