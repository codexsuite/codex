import { useMemo } from 'react';
import { useTheme } from './index';
import type { Theme } from './types';

/** Styles built from the current theme. `factory` must be a stable (module-level) function, so it runs once per theme. */
export function useStyles<T>(factory: (theme: Theme) => T): { theme: Theme; styles: T } {
  const theme = useTheme();
  const styles = useMemo(() => factory(theme), [factory, theme]);
  return { theme, styles };
}
