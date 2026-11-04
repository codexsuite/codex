import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate, Outlet, useLocation, useMatch, useParams } from 'react-router-dom';
import { parseBookRole } from '@codex/core/api/bookRole';
import { useBook } from '../api/booksQueries';
import { ApiRequestError } from '@codex/core/api/client';
import { CategoryHeadingMenu } from '../features/kb/CategoryHeadingMenu';
import { EntryWindow } from '../features/kb/EntryWindow';
import { useEntryHash } from '../features/kb/useEntryHash';
import { useKbSidebar } from '../features/kb/useKbSidebar';
import { useBookRealtime } from '../realtime/useBookRealtime';
import { testIds } from '../testIds';
import { BookBar } from './BookBar';
import { bookAccess, BookAccessContext } from './bookAccess';
import { bookScreens, type BookScreen } from '@codex/core/shell/deepLink';
import { ErrorState } from './ErrorState';
import { KbSidebarFrame } from './KbSidebarFrame';
import { bookScreenLink } from '@codex/core/shell/links';
import { shellTestIds } from './shellTestIds';
import { getLastScreen, setLastScreen } from '@codex/core/shell/stores';
import { useHighlight } from './useHighlight';
import { useDeepLink } from './useDeepLink';

// The chrome every book screen sits in: the book bar (with the slots), the Knowledge base sidebar on Plan and
// Write, and the routed lane page. Loads the book header once; an unknown book is an error state, not a crash.
export function BookWorkspace() {
  const { bookId = '' } = useParams();
  const book = useBook(bookId);
  useBookRealtime(bookId);
  const deepLink = useDeepLink();
  const { pathname } = useLocation();
  const screen = deepLink.ok && deepLink.link.screen !== 'library' ? deepLink.link.screen : null;
  const kb = deepLink.ok ? deepLink.link.kb : undefined;
  const [highlighted, setHighlighted] = useHighlight(bookId, kb);
  const [kbQuery, setKbQuery] = useState('');
  const [kbHidden, setKbHidden] = useState<string[]>([]);
  const sidebar = useKbSidebar(bookId, kbQuery, kbHidden);
  const entryId = deepLink.ok ? deepLink.link.entry : undefined;
  const [draftCategoryId, setDraftCategoryId] = useState<string | null>(null);
  const entryHash = useEntryHash();
  // On Plan and Write the book bar toggles the Knowledge base sidebar (open to start); Layout starts without it.
  const [planSidebar, setPlanSidebar] = useState(true);
  const [writeSidebar, setWriteSidebar] = useState(true);
  const [layoutSidebar, setLayoutSidebar] = useState(false);
  const hasSidebar =
    (screen === 'plan' && planSidebar) ||
    (screen === 'write' && writeSidebar) ||
    (screen === 'layout' && layoutSidebar);
  // A click highlights the entry (store and `#kb=`) and opens its window (`#entry=`); the open entry's click
  // closes the window. Other hash keys are kept.
  const selectEntry = (id: string) => {
    setHighlighted(id);
    setDraftCategoryId(null);
    if (entryId === id) {
      entryHash.close();
    } else {
      entryHash.open(id);
    }
  };
  // Every screen reads the role from here (#80): the header's, so a new one (AccessChanged, a refetch) re-renders them.
  const role = book.data?.role;
  const ownerName = book.data?.owner.displayName ?? '';
  const access = useMemo(
    () => bookAccess(role === undefined ? 'view' : parseBookRole(role), ownerName),
    [role, ownerName],
  );
  // `/books/:id` alone has no screen to parse: its index route redirects to the last screen.
  const isIndex = useMatch('/books/:bookId') !== null;

  // Settings is never the screen a book opens on (design/README.md -> Book settings patterns).
  useEffect(() => {
    if (book.isSuccess && screen !== null && screen !== 'settings') setLastScreen(bookId, screen);
  }, [book.isSuccess, bookId, screen]);

  if (book.isError) {
    const unknown = book.error instanceof ApiRequestError && book.error.code === 'not_found';
    return (
      <ErrorState
        testId={testIds.shell.errorUnknownBook}
        title={unknown ? "That book doesn't exist" : 'The book could not be loaded'}
        detail={book.error.message}
      />
    );
  }
  if (!book.data) return null;

  return (
    <BookAccessContext.Provider value={access}>
      <div className="flex h-screen flex-col">
        <BookBar
          book={book.data}
          sidebar={
            screen === 'layout'
              ? { open: layoutSidebar, onToggle: () => setLayoutSidebar((open) => !open) }
              : screen === 'write'
                ? { open: writeSidebar, onToggle: () => setWriteSidebar((open) => !open) }
                : screen === 'plan'
                  ? { open: planSidebar, onToggle: () => setPlanSidebar((open) => !open) }
                  : undefined
          }
        />
        <div className="flex min-h-0 flex-1">
          {hasSidebar && (
            <KbSidebarFrame
              sections={sidebar.sections}
              totalEntries={sidebar.totalEntries}
              status={sidebar.status}
              errorMessage={sidebar.error?.message}
              query={kbQuery}
              onQueryChange={setKbQuery}
              categories={sidebar.categories}
              hiddenCategories={kbHidden}
              onHiddenCategoriesChange={setKbHidden}
              renderCategoryMenu={(group) => (
                <CategoryHeadingMenu
                  bookId={bookId}
                  categoryId={group.id}
                  name={group.name}
                  colour={group.colour}
                />
              )}
              highlighted={highlighted}
              readOnly={!access.canWrite}
              onSelect={selectEntry}
              onNewEntry={(categoryId) => {
                entryHash.close();
                setDraftCategoryId(categoryId);
              }}
            />
          )}
          <main className="min-w-0 flex-1 overflow-auto">
            {deepLink.ok || isIndex ? (
              <Outlet />
            ) : (
              <ErrorState
                testId={testIds.shell.errorBadLink}
                title="This link doesn't make sense"
                detail={deepLink.error}
                action={
                  <Link
                    to={pathname}
                    data-testid={shellTestIds.errorRecoverLink}
                    className="text-sm font-medium text-accent no-underline hover:underline"
                  >
                    Open the screen without the link target
                  </Link>
                }
              />
            )}
          </main>
        </div>
        {hasSidebar && (entryId !== undefined || draftCategoryId !== null) && (
          <EntryWindow
            bookId={bookId}
            entryId={entryId}
            draftCategoryId={draftCategoryId}
            onDraftClose={() => setDraftCategoryId(null)}
          />
        )}
      </div>
    </BookAccessContext.Provider>
  );
}

// `/books/:id` alone opens where the reader left off, else Plan.
export function BookIndexRedirect() {
  const { bookId = '' } = useParams();
  const last: BookScreen = getLastScreen(bookId) ?? bookScreens[0];
  return <Navigate to={bookScreenLink(bookId, last)} replace />;
}
