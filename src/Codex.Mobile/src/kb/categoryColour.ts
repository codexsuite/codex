import type { CategoryName, Theme } from '~/theme';

/** The theme colour of a category token (`category-person`, or just `person`); muted when the category has none. */
export function categoryColour(theme: Theme, token: string | null): string {
  const name = (token ?? '').replace(/^category-/, '');
  return name in theme.category ? theme.category[name as CategoryName] : theme.colors.muted;
}
