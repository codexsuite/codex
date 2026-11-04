import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { login, meKey, safeReturnUrl } from '@codex/core/api/auth';
import { ApiRequestError } from '@codex/core/api/client';
import { field, label, primaryButton } from './styles';
import { authTestIds } from './testIds';

// /login: email and password, errors inline. There is no sign-up; an admin adds users (/settings/users). After
// signing in the page goes to `returnUrl` when it is a path on this site, else the Library.
export function LoginPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const me = await login(email, password);
      queryClient.setQueryData(meKey, me);
      navigate(safeReturnUrl(params.get('returnUrl')), { replace: true });
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'Could not sign in. Try again.');
      setBusy(false);
    }
  }

  return (
    <div data-testid={authTestIds.loginPage} className="flex min-h-screen flex-col bg-bg text-text">
      <header className="flex h-[var(--bar-height)] items-center gap-2 border-b border-border px-4">
        <h1 className="m-0 text-md font-semibold text-text">Codex</h1>
        <span className="flex-1" />
      </header>
      <form
        onSubmit={submit}
        className="m-auto flex w-[min(380px,100%)] flex-col rounded-lg border border-border bg-surface"
      >
        <h2 className="m-0 border-b border-border px-4 py-3 text-md font-semibold">Sign in</h2>
        <div className="grid grid-cols-[80px_1fr] items-start gap-x-3 gap-y-3 p-4">
          <label htmlFor="login-email" className={label}>
            Email
          </label>
          <input
            id="login-email"
            data-testid={authTestIds.loginEmail}
            type="email"
            autoComplete="username"
            required
            autoFocus
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={field}
          />
          <label htmlFor="login-password" className={label}>
            Password
          </label>
          <input
            id="login-password"
            data-testid={authTestIds.loginPassword}
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={field}
          />
        </div>
        {error !== null && (
          <p
            role="alert"
            data-testid={authTestIds.loginError}
            className="m-0 px-4 pb-3 text-xs text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex items-center gap-2 border-t border-border bg-bg px-4 py-3">
          <span className="flex-1 text-xs text-muted">No account? Ask your Codex admin.</span>
          <button
            type="submit"
            data-testid={authTestIds.loginSubmit}
            disabled={busy}
            className={primaryButton}
          >
            Sign in
          </button>
        </div>
      </form>
    </div>
  );
}
