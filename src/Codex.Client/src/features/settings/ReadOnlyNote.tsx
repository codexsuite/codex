import type { BookRole } from '@codex/core/api/bookRole';
import { CommentIcon, EyeIcon } from './accessIcons';
import { settingsTestIds } from './testIds';

// Above the cards for a viewer or commenter (#80): why nothing on the page can be changed, and whom to ask.
export function ReadOnlyNote({
  role,
  owner,
  title,
}: {
  role: BookRole;
  owner: string;
  title: string;
}) {
  const view = role === 'view';
  return (
    <div
      data-testid={settingsTestIds.readOnlyNote}
      role="note"
      className="flex items-start gap-2 rounded-md border border-info/35 bg-info/[0.07] px-2.5 py-2 text-xs text-text [&_b]:font-semibold"
    >
      {view ? (
        <EyeIcon className="mt-px size-3.5 flex-none text-info" />
      ) : (
        <CommentIcon className="mt-px size-3.5 flex-none text-info" />
      )}
      <p className="m-0">
        <b>{view ? 'View only.' : 'Comment only.'}</b> {owner} shared {title} with you{' '}
        {view ? 'to read' : 'to read and comment on'}. These are its settings as they are now; ask{' '}
        {owner} for edit access to change them.
      </p>
    </div>
  );
}
