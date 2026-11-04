import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  createUser,
  listUsers,
  resetPassword,
  revokeUserTokens,
  updateUser,
  usersKey,
  type AdminUser,
  type OneTimePassword,
  type UpdateUserRequest,
} from '@codex/core/api/auth';
import { useMe } from '../../api/authQueries';
import { ApiRequestError } from '@codex/core/api/client';
import { ErrorState } from '../../shell/ErrorState';
import { shellTestIds } from '../../shell/shellTestIds';
import { libraryLink } from '@codex/core/shell/links';
import { AuthPageBar } from './AuthPageBar';
import { button, field, primaryButton } from './styles';
import { authTestIds } from './testIds';

const statusTone: Record<AdminUser['status'], string> = {
  active: 'text-ok',
  disabled: 'text-muted',
  locked: 'text-warn',
};

// Shown once after Add user or Reset password: the server never shows it again, and Codex sends no email.
function OneTimePasswordBox({
  created,
  onDismiss,
}: {
  created: OneTimePassword;
  onDismiss: () => void;
}) {
  const [copied, setCopied] = useState(false);
  return (
    <div
      role="status"
      className="flex flex-col gap-2 rounded-lg border border-border bg-surface p-3"
    >
      <p className="m-0 text-sm text-text">
        One-time password for <strong className="font-semibold">{created.user.email}</strong>. Pass
        it on yourself; it is shown only now, and they choose their own password when they first
        sign in.
      </p>
      <div className="flex items-center gap-2">
        <code
          data-testid={authTestIds.oneTimePassword}
          className="rounded border border-border bg-bg px-2 py-1 font-mono text-sm text-text"
        >
          {created.oneTimePassword}
        </code>
        <button
          type="button"
          data-testid={authTestIds.oneTimePasswordCopy}
          className={button}
          onClick={() => {
            void navigator.clipboard.writeText(created.oneTimePassword).then(() => setCopied(true));
          }}
        >
          {copied ? 'Copied' : 'Copy'}
        </button>
        <span className="flex-1" />
        <button
          type="button"
          data-testid={authTestIds.oneTimePasswordDismiss}
          className={button}
          onClick={onDismiss}
        >
          Done
        </button>
      </div>
    </div>
  );
}

function AddUserForm({ onCreated }: { onCreated: (created: OneTimePassword) => void }) {
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onCreated(await createUser(email, name));
      setEmail('');
      setName('');
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'Could not add the user. Try again.');
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
      <input
        aria-label="Email"
        placeholder="Email"
        type="email"
        required
        data-testid={authTestIds.addUserEmail}
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        className={`${field} w-64`}
      />
      <input
        aria-label="Display name"
        placeholder="Display name"
        required
        data-testid={authTestIds.addUserName}
        value={name}
        onChange={(e) => setName(e.target.value)}
        className={`${field} w-48`}
      />
      <button type="submit" data-testid={authTestIds.addUserSubmit} className={primaryButton}>
        Add user
      </button>
      {error !== null && (
        <p
          role="alert"
          data-testid={authTestIds.usersError}
          className="m-0 w-full text-xs text-danger"
        >
          {error}
        </p>
      )}
    </form>
  );
}

// /settings/users, admins only (the server answers 403 to anyone else): every user, Add user, and per row Make or
// Remove admin, Disable or Enable, Unlock, Reset password and Revoke tokens (their personal access tokens for MCP, #32).
// There is no delete: a user owns books.
export function UsersPage() {
  const me = useMe();
  const queryClient = useQueryClient();
  const users = useQuery({
    queryKey: usersKey,
    queryFn: ({ signal }) => listUsers(signal),
    enabled: me.data?.isAdmin === true,
  });
  const [shown, setShown] = useState<OneTimePassword | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (me.data !== undefined && !me.data.isAdmin) {
    return (
      <div className="flex min-h-screen flex-col">
        <AuthPageBar title="Users" />
        <ErrorState
          testId={shellTestIds.notFound}
          title="Only admins can manage users"
          action={<Link to={libraryLink()}>Back to Library</Link>}
        />
      </div>
    );
  }

  async function act(run: () => Promise<unknown>) {
    setError(null);
    try {
      await run();
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'That did not work. Try again.');
    }
    await queryClient.invalidateQueries({ queryKey: usersKey });
  }

  const update = (user: AdminUser, request: UpdateUserRequest) =>
    act(() => updateUser(user.id, request));
  const reset = (user: AdminUser) =>
    act(async () => {
      setShown(await resetPassword(user.id));
    });
  const revokeTokens = (user: AdminUser) => act(() => revokeUserTokens(user.id));
  const isSelf = (user: AdminUser) => user.id === me.data?.id;
  const cell = 'border-b border-border px-2 py-1.5 text-sm';
  const rowButton =
    'h-7 rounded border border-border bg-transparent px-2 text-xs text-text disabled:opacity-50';

  return (
    <div data-testid={authTestIds.usersPage} className="flex min-h-screen flex-col">
      <AuthPageBar title="Users" />
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-3 p-4">
        <AddUserForm
          onCreated={(created) => {
            setShown(created);
            void queryClient.invalidateQueries({ queryKey: usersKey });
          }}
        />
        {shown !== null && <OneTimePasswordBox created={shown} onDismiss={() => setShown(null)} />}
        {error !== null && (
          <p role="alert" data-testid={authTestIds.usersError} className="m-0 text-xs text-danger">
            {error}
          </p>
        )}
        <table className="w-full border-collapse text-left">
          <thead>
            <tr className="text-xs text-muted">
              <th className={`${cell} font-medium`}>Name</th>
              <th className={`${cell} font-medium`}>Email</th>
              <th className={`${cell} font-medium`}>Admin</th>
              <th className={`${cell} font-medium`}>Status</th>
              <th className={`${cell} font-medium`}>Last sign-in</th>
              <th className={`${cell} font-medium`}>Created</th>
              <th className={`${cell} font-medium`}>Tokens</th>
              <th className={`${cell} font-medium`} aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {users.data?.map((user) => (
              <tr
                key={user.id}
                data-testid={authTestIds.userRow(user.id)}
                className="hover:bg-accent-soft"
              >
                <td className={`${cell} text-text`}>{user.displayName}</td>
                <td className={`${cell} text-text`}>{user.email}</td>
                <td className={`${cell} text-text`}>{user.isAdmin ? 'Admin' : ''}</td>
                <td
                  data-testid={authTestIds.userStatus(user.id)}
                  className={`${cell} ${statusTone[user.status]}`}
                >
                  {user.status === 'active' && user.mustChangePassword
                    ? 'active · must change password'
                    : user.status}
                </td>
                <td className={`${cell} text-muted`}>{user.lastLoginLabel ?? 'Never'}</td>
                <td className={`${cell} text-muted`}>{user.createdLabel}</td>
                <td
                  data-testid={authTestIds.userTokenCount(user.id)}
                  className={`${cell} text-muted`}
                >
                  {user.tokenCount}
                </td>
                <td className={`${cell} whitespace-nowrap text-right`}>
                  <span className="inline-flex gap-1">
                    <button
                      type="button"
                      data-testid={authTestIds.userToggleAdmin(user.id)}
                      className={rowButton}
                      disabled={isSelf(user)}
                      onClick={() => update(user, { isAdmin: !user.isAdmin })}
                    >
                      {user.isAdmin ? 'Remove admin' : 'Make admin'}
                    </button>
                    <button
                      type="button"
                      data-testid={authTestIds.userToggleDisabled(user.id)}
                      className={rowButton}
                      disabled={isSelf(user)}
                      onClick={() => update(user, { disabled: user.status !== 'disabled' })}
                    >
                      {user.status === 'disabled' ? 'Enable' : 'Disable'}
                    </button>
                    {user.status === 'locked' && (
                      <button
                        type="button"
                        data-testid={authTestIds.userUnlock(user.id)}
                        className={rowButton}
                        onClick={() => update(user, { unlock: true })}
                      >
                        Unlock
                      </button>
                    )}
                    <button
                      type="button"
                      data-testid={authTestIds.userResetPassword(user.id)}
                      className={rowButton}
                      onClick={() => reset(user)}
                    >
                      Reset password
                    </button>
                    <button
                      type="button"
                      data-testid={authTestIds.userRevokeTokens(user.id)}
                      className={rowButton}
                      disabled={user.tokenCount === 0}
                      onClick={() => revokeTokens(user)}
                    >
                      Revoke tokens
                    </button>
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
