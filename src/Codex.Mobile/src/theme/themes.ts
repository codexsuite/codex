// Pure theme values built from the generated tokens. No react-native import, so Node tests can load it.
import type { ColorName, StatusName, Theme } from './types';
import {
  categoryColors,
  darkColors,
  fontSize,
  fontWeight,
  lightColors,
  radius,
  sizes,
  space,
  statusColors,
} from './tokens.generated';

function build(scheme: Theme['scheme']): Theme {
  const colors: Record<ColorName, string> = scheme === 'dark' ? darkColors : lightColors;
  const status = Object.fromEntries(
    Object.entries(statusColors).map(([name, colorName]) => [name, colors[colorName]]),
  ) as Record<StatusName, string>;
  return {
    scheme,
    colors,
    text: fontSize,
    weight: fontWeight,
    size: sizes,
    space,
    radius,
    status,
    category: categoryColors[scheme],
  };
}

export const lightTheme: Theme = build('light');
export const darkTheme: Theme = build('dark');

export function themeFor(scheme: Theme['scheme'] | null | undefined): Theme {
  return scheme === 'dark' ? darkTheme : lightTheme;
}
