import { Link, useRoutes, type RouteObject } from 'react-router-dom';
import { App } from './App';
import { AccountPage } from './features/auth/AccountPage';
import { LoginPage } from './features/auth/LoginPage';
import { PasswordPage } from './features/auth/PasswordPage';
import { AiSettingsPage } from './features/ai/AiSettingsPage';
import { UsersPage } from './features/auth/UsersPage';
import { LayoutPage } from './features/layout/LayoutPage';
import { LibraryPage } from './features/library/LibraryPage';
import { PlanPage } from './features/plan/PlanPage';
import { ReferencesPage } from './features/references/ReferencesPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { WritePage } from './features/write/WritePage';
import { BookIndexRedirect, BookWorkspace } from './shell/BookWorkspace';
import { ErrorState } from './shell/ErrorState';
import { shellTestIds } from './shell/shellTestIds';
import { libraryLink } from '@codex/core/shell/links';

// Every route in the app (docs/navigation.md). Lane pages are pre-registered here so a lane never edits this file.
const routes: RouteObject[] = [
  { path: 'login', element: <LoginPage /> },
  {
    element: <App />,
    children: [
      { index: true, element: <LibraryPage /> },
      { path: 'account', element: <AccountPage /> },
      { path: 'account/password', element: <PasswordPage /> },
      { path: 'settings/ai', element: <AiSettingsPage /> },
      { path: 'settings/users', element: <UsersPage /> },
      {
        path: 'books/:bookId',
        element: <BookWorkspace />,
        children: [
          { index: true, element: <BookIndexRedirect /> },
          { path: 'plan', element: <PlanPage /> },
          { path: 'write', element: <WritePage /> },
          { path: 'references', element: <ReferencesPage /> },
          { path: 'layout', element: <LayoutPage /> },
          { path: 'settings', element: <SettingsPage /> },
        ],
      },
      {
        path: '*',
        element: (
          <ErrorState
            testId={shellTestIds.notFound}
            title="That page doesn't exist"
            action={<Link to={libraryLink()}>Back to Library</Link>}
          />
        ),
      },
    ],
  },
];

export function AppRoutes() {
  return useRoutes(routes);
}
