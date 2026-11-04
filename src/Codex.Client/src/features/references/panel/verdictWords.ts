import type { SourceDetail } from '@codex/core/api/referenceSources';

// The clearance reason codes in the panel's words (the table has its own, shorter wording in sourceStatus.ts).
export function verdictReason(source: SourceDetail): string {
  switch (source.verdict.reason) {
    case 'permission_drafted':
      return 'Permission drafted';
    case 'permission_sent':
      return 'Permission asked, no reply';
    case 'permission_refused':
      return 'Permission refused';
    case 'quote_over_limit':
      return `Quoted prose: ${source.quotedWords} of ${source.limit} words`;
    case 'lyric':
      return 'Lyrics need permission';
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
      return 'Signed off';
    default:
      return source.verdict.reason;
  }
}

// Why permission is needed, as the failing check advises (design/screens/references.html `assess`).
function permissionNote(reason: string): string {
  switch (reason) {
    case 'quote_over_limit':
      return 'Over the limit for quoting without permission. Shorten, paraphrase, or ask.';
    case 'lyric':
      return 'Lyrics and poetry need permission, even for a line or two.';
    case 'figure':
      return 'Reproducing or adapting a figure needs permission.';
    case 'permission_sent':
      return 'Waiting on the rights holder. Until they reply, this blocks print export.';
    case 'permission_refused':
      return 'The rights holder said no. Cut the passage, replace it, or ask again.';
    default:
      return 'A use needs permission that has not been granted. Until it is, this blocks print export.';
  }
}

// What to do next, in a sentence.
export function verdictNote(source: SourceDetail): string {
  switch (source.verdict.action) {
    case 'remove':
      return 'No marker cites this source. Insert one in Write with / → Source reference, or remove it. Uncited sources stay out of the bibliography.';
    case 'permission':
      return permissionNote(source.verdict.reason);
    case 'accept_fair_use':
      return 'The licence allows a partial quote only with a recorded fair-use decision.';
    case 'set_licence':
      return 'Record the licence so the checks can run.';
    case 'fill_details':
      return 'The citation is missing required fields. Fill them in below.';
    case 'recheck':
      return 'An identifier redirects or was not found. Re-check the links, or update the address.';
    case 'sign_off':
      return source.verdict.reason === 'new_uses_since_signoff'
        ? 'A new passage cites this source. Read it below, then clear the source again.'
        : 'Every check passes. Clearing records who signed it off, and a later change reopens it.';
    case 'reopen':
      return 'Every use is within the publisher rules.';
    default:
      return '';
  }
}
