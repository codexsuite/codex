// The shell's and the primitives' default data-testid values (prefix mobile-shell-). A primitive takes a `testID` prop
// so a lane can give its own element its own id; the ids of the primitive's inner parts are derived from it.
export const shellTestIds = {
  sheet: 'mobile-shell-sheet',
  pushBar: 'mobile-shell-push-bar',
  row: 'mobile-shell-row',
  chip: 'mobile-shell-chip',
  button: 'mobile-shell-button',
  textField: 'mobile-shell-text-field',
  switchRow: 'mobile-shell-switch-row',
  sectionHeader: 'mobile-shell-section-header',
  emptyState: 'mobile-shell-empty-state',
  errorState: 'mobile-shell-error-state',
  cover: 'mobile-shell-cover',
  bookBar: 'mobile-shell-book-bar',
  tabBar: 'mobile-shell-tab-bar',
  saveDot: 'mobile-shell-save-dot',
  tab: (name: string) => `mobile-shell-tab-${name}`,
  screen: (name: string) => `mobile-shell-screen-${name}`,
} as const;

/** `${base}-close`, `${base}-title`: the ids of a primitive's parts. */
export const part = (base: string, name: string) => `${base}-${name}`;
