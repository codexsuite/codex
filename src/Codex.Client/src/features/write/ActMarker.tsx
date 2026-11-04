import type { ActView } from '@codex/core/features/write/manuscriptView';
import { writeTestIds } from './testIds';

interface ActMarkerProps {
  view: ActView;
}

// "Act 2 · The Turn", with the act's part-opener art as a small thumbnail when it has one (set in Plan).
export function ActMarker({ view }: ActMarkerProps) {
  const opener = view.act.opener;
  return (
    <div
      data-testid={writeTestIds.actMarker(view.act.id)}
      className="mt-5 mb-2 flex items-center gap-2 text-2xs font-semibold tracking-wider text-muted uppercase after:h-px after:flex-1 after:bg-border after:content-['']"
    >
      {opener?.webUrl && (
        <img
          data-testid={writeTestIds.openerThumb(view.act.id)}
          src={opener.webUrl}
          alt=""
          title={opener.caption ? `Opener art: ${opener.caption}` : 'Opener art'}
          className="h-5 w-7 rounded-xs object-cover"
        />
      )}
      {view.marker}
    </div>
  );
}
