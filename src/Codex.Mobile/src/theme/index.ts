// The app's theme (#35), generated from design/tokens.json at the touch density (see scripts/generate-theme.mjs).
import { useColorScheme } from 'react-native';
import { themeFor } from './themes';
import type { Theme } from './types';

export type { CategoryName, ColorName, StatusName, Theme } from './types';
export { darkTheme, lightTheme, themeFor } from './themes';

/** The theme for the device's colour scheme (light when the scheme is unknown). */
export function useTheme(): Theme {
  return themeFor(useColorScheme());
}
