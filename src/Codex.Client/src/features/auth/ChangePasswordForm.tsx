import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { changePassword, meKey } from '@codex/core/api/auth';
import { ApiRequestError } from '@codex/core/api/client';
import { field, label, primaryButton } from './styles';
import { authTestIds } from './testIds';

const minLength = 10;

// Current password, new password twice. Changing it signs every other session out (the server keeps this one).
export function ChangePasswordForm({ onChanged }: { onChanged: () => void }) {
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (next !== confirm) {
      setError('The new passwords do not match.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      queryClient.setQueryData(meKey, await changePassword(current, next));
      setCurrent('');
      setNext('');
      setConfirm('');
      onChanged();
    } catch (e) {
      setError(
        e instanceof ApiRequestError ? e.message : 'Could not change the password. Try again.',
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-col">
      <div className="grid grid-cols-[150px_1fr] items-start gap-x-3 gap-y-3">
        <label htmlFor="password-current" className={label}>
          Current password
        </label>
        <input
          id="password-current"
          data-testid={authTestIds.passwordCurrent}
          type="password"
          autoComplete="current-password"
          required
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          className={field}
        />
        <label htmlFor="password-new" className={label}>
          New password
        </label>
        <div className="flex flex-col gap-1">
          <input
            id="password-new"
            data-testid={authTestIds.passwordNew}
            type="password"
            autoComplete="new-password"
            required
            minLength={minLength}
            value={next}
            onChange={(e) => setNext(e.target.value)}
            className={field}
          />
          <span className="text-xs text-muted">At least {minLength} characters</span>
        </div>
        <label htmlFor="password-confirm" className={label}>
          Repeat new password
        </label>
        <input
          id="password-confirm"
          data-testid={authTestIds.passwordConfirm}
          type="password"
          autoComplete="new-password"
          required
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          className={field}
        />
      </div>
      {error !== null && (
        <p
          role="alert"
          data-testid={authTestIds.passwordError}
          className="m-0 pt-3 text-xs text-danger"
        >
          {error}
        </p>
      )}
      <div className="flex justify-end pt-3">
        <button
          type="submit"
          data-testid={authTestIds.passwordSubmit}
          disabled={busy}
          className={primaryButton}
        >
          Change password
        </button>
      </div>
    </form>
  );
}
