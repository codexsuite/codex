import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { meKey, updateMe, type Me } from '@codex/core/api/auth';
import { useMe } from '../../api/authQueries';
import { ApiRequestError } from '@codex/core/api/client';
import { AgentAccessSection } from './AgentAccessSection';
import { AuthPageBar } from './AuthPageBar';
import { ChangePasswordForm } from './ChangePasswordForm';
import { DeviceSessionsSection } from './DeviceSessionsSection';
import { field, label, primaryButton } from './styles';
import { authTestIds } from './testIds';

function NameForm({ me }: { me: Me }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(me.displayName);
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      const updated = await updateMe(name);
      queryClient.setQueryData(meKey, updated);
      setName(updated.displayName);
      setMessage({ text: 'Saved', error: false });
    } catch (e) {
      setMessage({
        text: e instanceof ApiRequestError ? e.message : 'Could not save. Try again.',
        error: true,
      });
    }
  }

  return (
    <form onSubmit={submit} className="grid grid-cols-[150px_1fr_auto] items-start gap-x-3 gap-y-2">
      <span className={label}>Email</span>
      <span className="col-span-2 pt-1 text-sm text-text">{me.email}</span>
      <label htmlFor="account-name" className={label}>
        Display name
      </label>
      <input
        id="account-name"
        data-testid={authTestIds.accountDisplayName}
        required
        maxLength={100}
        value={name}
        onChange={(e) => {
          setName(e.target.value);
          setMessage(null);
        }}
        className={field}
      />
      <button type="submit" data-testid={authTestIds.accountSaveName} className={primaryButton}>
        Save
      </button>
      {message !== null && (
        <p
          role={message.error ? 'alert' : 'status'}
          data-testid={authTestIds.accountNameSaved}
          className={`col-start-2 m-0 text-xs ${message.error ? 'text-danger' : 'text-ok'}`}
        >
          {message.text}
        </p>
      )}
    </form>
  );
}

// /account: your display name, your password and your personal access tokens for agents (Agent access) and the phones signed in to your account.
export function AccountPage() {
  const me = useMe();
  const [passwordSaved, setPasswordSaved] = useState(false);
  const card = 'flex flex-col gap-3 rounded-lg border border-border bg-surface p-4';
  return (
    <div data-testid={authTestIds.accountPage} className="flex min-h-screen flex-col">
      <AuthPageBar title="Account" />
      <div className="mx-auto flex w-[min(720px,100%)] flex-col gap-4 p-4">
        <section className={card}>
          <h2 className="m-0 text-md font-semibold">Profile</h2>
          {me.data !== undefined && <NameForm me={me.data} />}
        </section>
        <section className={card}>
          <h2 className="m-0 text-md font-semibold">Password</h2>
          {passwordSaved && (
            <p
              role="status"
              data-testid={authTestIds.passwordSaved}
              className="m-0 text-sm text-ok"
            >
              Password changed. Your other sessions have been signed out.
            </p>
          )}
          <ChangePasswordForm onChanged={() => setPasswordSaved(true)} />
        </section>
        <AgentAccessSection />
        <DeviceSessionsSection />
      </div>
    </div>
  );
}
