import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMe } from '../../api/authQueries';
import { libraryLink } from '@codex/core/shell/links';
import { AuthPageBar } from './AuthPageBar';
import { ChangePasswordForm } from './ChangePasswordForm';
import { authTestIds } from './testIds';

// /account/password. After an admin adds a user or resets their password, every route lands here (AuthGate) until
// the one-time password is replaced; then the Library opens.
export function PasswordPage() {
  const me = useMe();
  const navigate = useNavigate();
  const [saved, setSaved] = useState(false);
  const mustChange = me.data?.mustChangePassword === true;

  return (
    <div data-testid={authTestIds.passwordPage} className="flex min-h-screen flex-col">
      <AuthPageBar title="Change password" />
      <section className="mx-auto flex w-[min(560px,100%)] flex-col gap-3 p-4">
        {mustChange && (
          <p
            data-testid={authTestIds.passwordMustChange}
            className="m-0 rounded border border-border bg-surface p-3 text-sm text-text"
          >
            You signed in with a one-time password. Choose your own password to continue.
          </p>
        )}
        {saved && (
          <p role="status" data-testid={authTestIds.passwordSaved} className="m-0 text-sm text-ok">
            Password changed. Your other sessions have been signed out.
          </p>
        )}
        <ChangePasswordForm
          onChanged={() =>
            mustChange ? navigate(libraryLink(), { replace: true }) : setSaved(true)
          }
        />
      </section>
    </div>
  );
}
