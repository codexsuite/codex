import { useMe } from '../../api/authQueries';
import { useSharing } from '../../api/sharingQueries';
import type { Sharing } from '@codex/core/api/types';
import { SettingsCard } from './SettingsCard';
import { SharingAdd } from './SharingAdd';
import { OwnerRow, PersonRow } from './SharingPeople';
import { grantsOf, type GrantScope } from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';
import { useSharingChanges } from './useSharingChanges';

const empty = 'm-0 mt-0.5 py-1.5 text-xs text-muted';
const subPara = 'm-0 -mt-0.5 mb-1 text-xs leading-[1.45] text-muted';

function Legend() {
  return (
    <p className="m-0 mt-2.5 text-xs leading-normal text-muted [&_b]:font-medium [&_b]:text-text">
      <b>Can view</b> opens every tab read-only. <b>Can comment</b> also adds comments.{' '}
      <b>Can edit</b> changes the text, plan, Knowledge base, references and settings. Only the
      owner can share, move, duplicate, publish or archive the book.
    </p>
  );
}

// Book settings → Sharing (#80): the owner, then one row per person, then Add people. A book in a series has two
// lists, This book and Whole series. The owner changes them; everyone else reads who has access, without emails.
export function SharingSection({ bookId, title }: { bookId: string; title: string }) {
  const sharing = useSharing(bookId);
  const { isOwner, ownerName } = useSettingsAccess(bookId);
  return (
    <SettingsCard
      section="sharing"
      title="Sharing"
      blurb={
        isOwner
          ? 'Who else can open this book, and what they can do. Changes save straight away and reach open tabs at once.'
          : `Who can open this book. Only ${ownerName}, the owner, can change this.`
      }
    >
      {sharing.isError && (
        <p
          data-testid={settingsTestIds.sharingError}
          role="alert"
          className="m-0 text-sm text-danger"
        >
          {sharing.error.message}
        </p>
      )}
      {sharing.data && (
        <SharingBody bookId={bookId} title={title} sharing={sharing.data} isOwner={isOwner} />
      )}
    </SettingsCard>
  );
}

interface BodyProps {
  bookId: string;
  title: string;
  sharing: Sharing;
  isOwner: boolean;
}

function SharingBody({ bookId, title, sharing, isOwner }: BodyProps) {
  const me = useMe().data?.id;
  const changes = useSharingChanges(bookId, sharing, title);
  const rows = (scope: GrantScope) =>
    grantsOf(sharing, scope).map((grant) => (
      <PersonRow
        key={grant.user.id}
        sharing={sharing}
        scope={scope}
        grant={grant}
        isOwner={isOwner}
        meId={me}
        onRole={(role) => void changes.change(scope, grant.user, role)}
        onRemove={() => void changes.remove(scope, grant.user)}
      />
    ));
  const add = (scope: GrantScope) =>
    isOwner && (
      <SharingAdd
        bookId={bookId}
        sharing={sharing}
        scope={scope}
        title={title}
        onAdd={(user, role) => changes.add(scope, user, role)}
      />
    );
  const error = changes.error && (
    <p
      data-testid={settingsTestIds.sharingError}
      role="alert"
      className="m-0 mt-1 text-sm text-danger"
    >
      {changes.error}
    </p>
  );

  if (sharing.series === null) {
    return (
      <div data-testid={settingsTestIds.sharing}>
        <ul className="m-0 grid list-none p-0">
          <OwnerRow sharing={sharing} meId={me} />
          {rows('book')}
        </ul>
        {sharing.book.length === 0 && (
          <p data-testid={settingsTestIds.sharingEmpty} className={empty}>
            Only you can open this book.
          </p>
        )}
        {error}
        {add('book')}
        <Legend />
      </div>
    );
  }
  const series = sharing.series;
  const sub = 'mt-3 border-t border-border pt-2.5';
  return (
    <div data-testid={settingsTestIds.sharing}>
      <ul className="m-0 grid list-none p-0">
        <OwnerRow sharing={sharing} meId={me} />
      </ul>
      {error}
      <div data-testid={settingsTestIds.sharingBookList} className={sub}>
        <h3 className="m-0 mb-1 text-sm font-semibold text-text">This book</h3>
        <p className={subPara}>People who see {title} and nothing else in the series.</p>
        {sharing.book.length > 0 ? (
          <ul className="m-0 grid list-none p-0">{rows('book')}</ul>
        ) : (
          <p data-testid={settingsTestIds.sharingEmpty} className={empty}>
            No one has access to only this book.
          </p>
        )}
        {add('book')}
      </div>
      <div data-testid={settingsTestIds.sharingSeriesList} className={sub}>
        <h3 className="m-0 mb-1 text-sm font-semibold text-text">Whole series: {series.title}</h3>
        <p className={subPara}>
          People who see every book in {series.title}, including books added later, plus the series’
          Knowledge base, labels and style guide.
          {isOwner ? ' You can change this from any book in the series.' : ''}
        </p>
        {series.grants.length > 0 ? (
          <ul className="m-0 grid list-none p-0">{rows('series')}</ul>
        ) : (
          <p data-testid={settingsTestIds.sharingEmpty} className={empty}>
            No one has access to the whole series.
          </p>
        )}
        {add('series')}
      </div>
      <Legend />
    </div>
  );
}
