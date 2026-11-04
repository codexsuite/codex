import type { StatusName } from '~/theme/types';
import type { Theme } from '~/theme';

const statusNames: readonly StatusName[] = [
  'planning',
  'draft',
  'writing',
  'revising',
  'published',
];

/** The dot colour for a status; a status the theme does not know gets the muted colour. */
export function statusColor(theme: Theme, status: string): string {
  const known = statusNames.find((name) => name === status);
  return known === undefined ? theme.colors.muted : theme.status[known];
}
