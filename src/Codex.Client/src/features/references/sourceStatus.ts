import type { SourceRow } from '@codex/core/api/referenceSources';

export type StatusFilter = 'all' | 'risk' | 'needs_check' | 'cleared' | 'not_cited';

interface StatusLook {
  label: string;
  /** The status filter chip's shorter label. */
  chip: string;
  /** Chip and group dot. */
  dot: string;
  text: string;
  meter: string;
  /** The colour for `--tone`, which tinted blocks (verdict, marker chip) mix from. */
  tone: string;
}

// Full class names, so Tailwind sees them. Risk first: the order groups and chips use.
export const statusLooks: Record<Exclude<StatusFilter, 'all'>, StatusLook> = {
  risk: {
    label: 'Copyright risk',
    chip: 'Risk',
    dot: 'bg-danger',
    text: 'text-danger',
    meter: 'bg-danger',
    tone: 'var(--color-danger)',
  },
  needs_check: {
    label: 'Needs check',
    chip: 'Needs check',
    dot: 'bg-warn',
    text: 'text-warn',
    meter: 'bg-warn',
    tone: 'var(--color-warn)',
  },
  cleared: {
    label: 'Cleared',
    chip: 'Cleared',
    dot: 'bg-ok',
    text: 'text-ok',
    meter: 'bg-ok',
    tone: 'var(--color-ok)',
  },
  not_cited: {
    label: 'Not cited',
    chip: 'Not cited',
    dot: 'bg-muted',
    text: 'text-muted',
    meter: 'bg-muted',
    tone: 'var(--color-muted)',
  },
};

export const statusOrder = ['risk', 'needs_check', 'cleared', 'not_cited'] as const;

export function lookOf(status: string): StatusLook | undefined {
  return (statusLooks as Record<string, StatusLook | undefined>)[status];
}

// The clearance reason codes (reference_clearance.reason) in words. An unknown code is shown as it is.
export function reasonLabel(row: SourceRow): string {
  switch (row.reason) {
    case 'no_markers':
      return 'No markers in the book';
    case 'permission_drafted':
      return 'Permission drafted';
    case 'permission_sent':
      return 'Permission asked · no reply';
    case 'permission_refused':
      return 'Permission refused';
    case 'quote_over_limit':
      return `Quoted prose: ${row.quotedWords} of ${row.limit} words`;
    case 'lyric':
      return 'Song lyrics need permission';
    case 'figure':
      return 'Figure needs permission';
    case 'licence_unknown':
      return 'Licence not recorded';
    case 'fair_use_undecided':
      return 'Fair use not decided';
    case 'citation_incomplete':
      return 'Citation incomplete';
    case 'identifier_failing':
      return 'Identifier needs checking';
    case 'ready_to_sign_off':
      return 'Ready to sign off';
    case 'new_uses_since_signoff':
      return 'New uses since cleared';
    case 'signed_off':
      return row.clearedLabel ?? 'Signed off';
    default:
      return row.reason;
  }
}
