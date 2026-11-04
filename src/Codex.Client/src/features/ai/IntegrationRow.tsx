import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
  deleteAiIntegration,
  makeDefaultAiIntegration,
  testAiIntegration,
  updateAiIntegration,
  type AiConnectionTest,
  type AiIntegration,
  type UpdateAiIntegrationRequest,
} from '@codex/core/api/ai';
import { ApiRequestError } from '@codex/core/api/client';
import { button, field, primaryButton } from '../auth/styles';
import { kindLabels, refreshAi } from './aiQueries';
import { ModelField } from './ModelField';
import { aiTestIds } from './testIds';

type Editing = 'rename' | 'key' | 'model' | 'remove' | null;

const small = 'h-7 rounded border border-border bg-transparent px-2 text-xs text-text';

// One connected provider: what it is, its key hint, default model and last use, and the actions on it. The new key of
// Replace key is held in this component's state only and cleared once saved.
export function IntegrationRow({ integration }: { integration: AiIntegration }) {
  const queryClient = useQueryClient();
  const { id } = integration;
  const [editing, setEditing] = useState<Editing>(null);
  const [text, setText] = useState('');
  const [tested, setTested] = useState<AiConnectionTest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(call: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await call();
      setEditing(null);
      setText('');
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'That did not work. Try again.');
    }
    await refreshAi(queryClient);
    setBusy(false);
  }

  const open = (mode: Exclude<Editing, null>, initial: string) => {
    setEditing(mode);
    setText(initial);
    setError(null);
  };
  const update = (request: UpdateAiIntegrationRequest) =>
    run(() => updateAiIntegration(id, request));
  const save = () => {
    if (editing === 'rename') return update({ label: text.trim() });
    if (editing === 'key') return update({ apiKey: text });
    return update({ defaultModel: text.trim() });
  };

  async function test() {
    setBusy(true);
    setError(null);
    try {
      setTested(await testAiIntegration(id));
    } catch (e) {
      setTested(null);
      setError(e instanceof ApiRequestError ? e.message : 'Could not test the connection.');
    }
    await refreshAi(queryClient);
    setBusy(false);
  }

  const model = integration.defaultModel ?? '';
  return (
    <li
      data-testid={aiTestIds.row(id)}
      data-kind={integration.kind}
      className="flex flex-col gap-2 border-b border-border py-3 last:border-b-0"
    >
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <strong className="text-sm font-semibold text-text">{integration.label}</strong>
        <span className="text-xs text-muted">{kindLabels[integration.kind]}</span>
        {integration.isDefault && (
          <span
            data-testid={aiTestIds.rowDefaultBadge(id)}
            className="rounded bg-accent-soft px-1.5 text-xs font-medium text-accent"
          >
            Default
          </span>
        )}
      </div>
      <dl className="m-0 grid grid-cols-[110px_1fr] gap-x-3 gap-y-0.5 text-sm">
        {integration.baseUrl !== null && (
          <>
            <dt className="text-muted">Base URL</dt>
            <dd className="m-0 break-all text-text">{integration.baseUrl}</dd>
          </>
        )}
        <dt className="text-muted">Key</dt>
        <dd className="m-0 text-text">
          <span data-testid={aiTestIds.rowKeyHint(id)} className="font-mono text-xs">
            {integration.keyHint}
          </span>{' '}
          <span className="text-muted">connected</span>
        </dd>
        <dt className="text-muted">Default model</dt>
        <dd data-testid={aiTestIds.rowModel(id)} className="m-0 text-text">
          {integration.defaultModel ?? 'None chosen'}
        </dd>
        <dt className="text-muted">Last used</dt>
        <dd data-testid={aiTestIds.rowLastUsed(id)} className="m-0 text-text">
          {integration.lastUsedLabel ?? 'Never'}
        </dd>
      </dl>
      {integration.lastError !== null && (
        <p data-testid={aiTestIds.rowLastError(id)} className="m-0 text-xs text-danger">
          Last error: {integration.lastError}
        </p>
      )}
      {tested !== null && (
        <p
          role="status"
          data-testid={aiTestIds.rowTestResult(id)}
          data-ok={tested.ok}
          className={`m-0 text-xs ${tested.ok ? 'text-ok' : 'text-danger'}`}
        >
          {tested.ok
            ? `Connected: ${tested.models.length} ${tested.models.length === 1 ? 'model' : 'models'}`
            : (tested.error ?? 'The connection failed.')}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          data-testid={aiTestIds.rowTest(id)}
          disabled={busy}
          className={small}
          onClick={() => void test()}
        >
          Test
        </button>
        {!integration.isDefault && (
          <button
            type="button"
            data-testid={aiTestIds.rowMakeDefault(id)}
            disabled={busy}
            className={small}
            onClick={() => void run(() => makeDefaultAiIntegration(id))}
          >
            Make default
          </button>
        )}
        <button
          type="button"
          data-testid={aiTestIds.rowChangeModel(id)}
          className={small}
          onClick={() => open('model', model)}
        >
          Change default model
        </button>
        <button
          type="button"
          data-testid={aiTestIds.rowReplaceKey(id)}
          className={small}
          onClick={() => open('key', '')}
        >
          Replace key
        </button>
        <button
          type="button"
          data-testid={aiTestIds.rowRename(id)}
          className={small}
          onClick={() => open('rename', integration.label)}
        >
          Rename
        </button>
        <button
          type="button"
          data-testid={aiTestIds.rowRemove(id)}
          className={small}
          onClick={() => open('remove', '')}
        >
          Remove
        </button>
      </div>
      {editing === 'remove' && (
        <div
          role="alertdialog"
          aria-label={`Remove ${integration.label}`}
          className="flex items-center gap-2"
        >
          <span className="text-sm text-text">Remove {integration.label}? Its key is deleted.</span>
          <button
            type="button"
            data-testid={aiTestIds.rowRemoveConfirm(id)}
            disabled={busy}
            className="h-7 rounded border-0 bg-danger px-3 text-xs text-white"
            onClick={() => void run(() => deleteAiIntegration(id))}
          >
            Remove
          </button>
          <button
            type="button"
            data-testid={aiTestIds.rowEditCancel(id)}
            className={small}
            onClick={() => setEditing(null)}
          >
            Cancel
          </button>
        </div>
      )}
      {(editing === 'rename' || editing === 'key' || editing === 'model') && (
        <form
          className="flex items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          {editing === 'model' ? (
            <ModelField
              testId={aiTestIds.rowEditInput(id)}
              label="Default model"
              models={integration.models}
              value={text}
              onChange={setText}
            />
          ) : (
            <input
              data-testid={aiTestIds.rowEditInput(id)}
              aria-label={editing === 'key' ? 'New API key' : 'Label'}
              type={editing === 'key' ? 'password' : 'text'}
              autoComplete="off"
              maxLength={editing === 'key' ? undefined : 100}
              value={text}
              onChange={(e) => setText(e.target.value)}
              className={`${field} min-w-0 flex-1`}
            />
          )}
          <button
            type="submit"
            data-testid={aiTestIds.rowEditSave(id)}
            disabled={busy || text.trim() === ''}
            className={primaryButton}
          >
            Save
          </button>
          <button
            type="button"
            data-testid={aiTestIds.rowEditCancel(id)}
            className={button}
            onClick={() => {
              setEditing(null);
              setText('');
            }}
          >
            Cancel
          </button>
        </form>
      )}
      {error !== null && (
        <p role="alert" className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
    </li>
  );
}
