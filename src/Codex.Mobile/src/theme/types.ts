// The theme contract (#35). Screens code against these names; add fields, never rename or remove.

export type ColorName =
  | 'bg'
  | 'surface'
  | 'surface-2'
  | 'border'
  | 'text'
  | 'muted'
  | 'accent'
  | 'accent-fg'
  | 'accent-soft'
  | 'ok'
  | 'warn'
  | 'info'
  | 'danger';

export type StatusName = 'planning' | 'draft' | 'writing' | 'revising' | 'published';

export type CategoryName = 'blog' | 'law' | 'story' | 'research' | 'person';

export interface Theme {
  scheme: 'light' | 'dark';
  colors: Record<ColorName, string>;
  /** design/tokens.json font.size with the touch overrides (base 15). */
  text: { '2xs': number; xs: number; sm: number; base: number; md: number; lg: number };
  weight: { regular: '400'; medium: '500'; semibold: '600' };
  /** Touch density: bar 44, control 36, row 44, tab 52, cover width 30. */
  size: { bar: number; control: number; row: number; tab: number; cover: number };
  space: { 1: number; 2: number; 3: number; 4: number; 5: number };
  radius: { xs: number; sm: number; md: number; lg: number };
  /** Project status to its resolved colour (tokens.json status maps names onto colors). */
  status: Record<StatusName, string>;
  /** Codex category colours (tokens.json category), separate from the UI colours. */
  category: Record<CategoryName, string>;
}
