export type Theme = 'light' | 'dark';

const storageKey = 'codex.theme';

/** The theme in effect: the forced one on <html data-theme>, else the OS preference. */
export function currentTheme(): Theme {
  const forced = document.documentElement.dataset.theme;
  if (forced === 'light' || forced === 'dark') return forced;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function setTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  try {
    window.localStorage.setItem(storageKey, theme);
  } catch {
    // the choice lasts for this page only
  }
}

/** Apply the stored choice, if any. Called once at startup. */
export function initTheme() {
  try {
    const stored = window.localStorage.getItem(storageKey);
    if (stored === 'light' || stored === 'dark') document.documentElement.dataset.theme = stored;
  } catch {
    // follow the OS
  }
}
