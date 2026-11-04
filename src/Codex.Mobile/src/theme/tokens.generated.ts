// Generated from design/tokens.json by scripts/generate-theme.mjs; do not edit.
// Regenerate with: node scripts/generate-theme.mjs

export const lightColors = {
  bg: '#e8eef7',
  surface: '#f7f9fd',
  'surface-2': '#dfe7f3',
  border: '#cdd9ea',
  text: '#1c2638',
  muted: '#52627c',
  accent: '#2563c9',
  'accent-fg': '#ffffff',
  'accent-soft': '#dae6fb',
  ok: '#3f8a3a',
  warn: '#b7701b',
  info: '#1d7fb8',
  danger: '#c2353b',
} as const;

export const darkColors = {
  bg: '#171e2d',
  surface: '#1f2839',
  'surface-2': '#29344a',
  border: '#334058',
  text: '#eef2f8',
  muted: '#a0aec6',
  accent: '#5cc8f0',
  'accent-fg': '#06192a',
  'accent-soft': '#163a57',
  ok: '#9fdc8a',
  warn: '#f4cf72',
  info: '#7aa7f5',
  danger: '#f08a8f',
} as const;

/** design/tokens.json font.size with the touch density overrides applied. */
export const fontSize = {
  '2xs': 11,
  xs: 12,
  sm: 13,
  base: 15,
  md: 16,
  lg: 22,
} as const;

export const fontWeight = {
  regular: '400',
  medium: '500',
  semibold: '600',
} as const;

/** Touch density sizes (density.touch). */
export const sizes = {
  bar: 44,
  control: 36,
  row: 44,
  tab: 52,
  cover: 30,
} as const;

export const space = {
  1: 4,
  2: 8,
  3: 12,
  4: 16,
  5: 24,
} as const;

export const radius = {
  xs: 3,
  sm: 6,
  md: 8,
  lg: 10,
} as const;

/** Codex category colours, separate from the UI colours. */
export const categoryColors = {
  light: {
    blog: '#a86200',
    law: '#3b5bdb',
    story: '#2f8a4c',
    research: '#7c4dce',
    person: '#c2416b',
  },
  dark: {
    blog: '#f2c063',
    law: '#93a8ff',
    story: '#8fd6a0',
    research: '#c3a6ff',
    person: '#f49ab8',
  },
} as const;

/** Project status to UI colour name. */
export const statusColors = {
  planning: 'info',
  draft: 'muted',
  writing: 'accent',
  revising: 'warn',
  published: 'ok',
} as const;
