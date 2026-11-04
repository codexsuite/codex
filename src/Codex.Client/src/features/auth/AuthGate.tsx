import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useMe } from '../../api/authQueries';
import { passwordLink } from './links';

// Every page but /login renders inside this. It waits for /api/auth/me (a 401 there sends the page to /login through
// the fetch wrapper), then, while the user must change a one-time password, keeps every route on /account/password.
// Any other failure renders the page anyway: the server enforces sign-in on each request, so this gate is only a
// convenience.
export function AuthGate({ children }: { children: ReactNode }) {
  const me = useMe();
  const location = useLocation();
  if (me.isPending) return null;
  if (me.data?.mustChangePassword === true && location.pathname !== passwordLink()) {
    return <Navigate to={passwordLink()} replace />;
  }
  return children;
}
