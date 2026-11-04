import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import {
  createAiIntegration,
  testAiConnection,
  type AiConnectionTest,
  type AiIntegrationKind,
} from '@codex/core/api/ai';
import { ApiRequestError } from '@codex/core/api/client';
import { button, field, label, primaryButton } from '../auth/styles';
import { kindLabels, refreshAi } from './aiQueries';
import { ModelField } from './ModelField';
import { aiTestIds } from './testIds';

interface ConnectDialogProps {
  localEndpointsAllowed: boolean;
  onClose: () => void;
}

const kinds: AiIntegrationKind[] = ['openai_compatible', 'openrouter', 'anthropic'];

const messageOf = (e: unknown, fallback: string) =>
  e instanceof ApiRequestError ? e.message : fallback;

// Connect a provider: pick the kind, give it a label (and a base URL for OpenAI-compatible) and a key, Test connection
// lists the provider's models, pick a default model and Save. The key lives in this component's state only: it is
// cleared after Save and never goes into the query cache.
export function ConnectDialog({ localEndpointsAllowed, onClose }: ConnectDialogProps) {
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [kind, setKind] = useState<AiIntegrationKind>('openai_compatible');
  const [name, setName] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [tested, setTested] = useState<AiConnectionTest | null>(null);
  const [model, setModel] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const url = kind === 'openai_compatible' ? baseUrl.trim() : null;
  // Changing anything the test depended on makes its answer stale.
  const edit = (change: () => void) => {
    change();
    setTested(null);
    setError(null);
  };

  async function test() {
    setBusy(true);
    setError(null);
    try {
      const result = await testAiConnection({ kind, baseUrl: url, apiKey });
      setTested(result);
      if (result.ok) setModel(result.models[0] ?? '');
    } catch (e) {
      setTested(null);
      setError(messageOf(e, 'Could not test the connection. Try again.'));
    }
    setBusy(false);
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (name.trim() === '' || apiKey === '') {
      setError('Give the connection a label and a key.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await createAiIntegration({
        kind,
        label: name.trim(),
        baseUrl: url,
        apiKey,
        defaultModel: model.trim() === '' ? null : model.trim(),
      });
      setApiKey('');
      await refreshAi(queryClient);
      onClose();
    } catch (e) {
      setError(messageOf(e, 'Could not save the connection. Try again.'));
      setBusy(false);
    }
  }

  return (
    <dialog
      ref={dialogRef}
      data-testid={aiTestIds.dialog}
      aria-label="Connect an AI provider"
      onClose={onClose}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
      className="m-auto w-[min(520px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form onSubmit={(event) => void save(event)} noValidate>
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 className="m-0 text-md font-semibold">Connect an AI provider</h3>
          <button
            type="button"
            data-testid={aiTestIds.dialogClose}
            aria-label="Close"
            onClick={onClose}
            className="border-0 bg-transparent text-lg text-muted"
          >
            ×
          </button>
        </div>
        <div className="grid grid-cols-[110px_1fr] items-start gap-x-3 gap-y-3 p-4">
          <span className={label}>Provider</span>
          <div role="radiogroup" aria-label="Provider" className="flex flex-wrap gap-2">
            {kinds.map((k) => (
              <button
                key={k}
                type="button"
                role="radio"
                aria-checked={kind === k}
                data-testid={aiTestIds.dialogKind(k)}
                onClick={() => edit(() => setKind(k))}
                className={`${button} ${kind === k ? 'border-accent bg-accent-soft text-accent' : ''}`}
              >
                {kindLabels[k]}
              </button>
            ))}
          </div>
          <label htmlFor="ai-connect-label" className={label}>
            Label
          </label>
          <input
            id="ai-connect-label"
            data-testid={aiTestIds.dialogLabel}
            maxLength={100}
            placeholder={kindLabels[kind]}
            autoComplete="off"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className={field}
          />
          {kind === 'openai_compatible' && (
            <>
              <label htmlFor="ai-connect-base-url" className={label}>
                Base URL
              </label>
              <div className="flex flex-col gap-1">
                <input
                  id="ai-connect-base-url"
                  data-testid={aiTestIds.dialogBaseUrl}
                  placeholder="https://api.example.com/v1"
                  autoComplete="off"
                  value={baseUrl}
                  onChange={(e) => edit(() => setBaseUrl(e.target.value))}
                  className={field}
                />
                {!localEndpointsAllowed && (
                  <span data-testid={aiTestIds.dialogBaseUrlHint} className="text-xs text-muted">
                    https:// only
                  </span>
                )}
              </div>
            </>
          )}
          <label htmlFor="ai-connect-api-key" className={label}>
            API key
          </label>
          <input
            id="ai-connect-api-key"
            data-testid={aiTestIds.dialogApiKey}
            type="password"
            autoComplete="off"
            value={apiKey}
            onChange={(e) => edit(() => setApiKey(e.target.value))}
            className={field}
          />
          <span />
          <div className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <button
                type="button"
                data-testid={aiTestIds.dialogTest}
                disabled={busy || apiKey === ''}
                onClick={() => void test()}
                className={button}
              >
                Test connection
              </button>
              {tested !== null && (
                <span
                  role="status"
                  data-testid={aiTestIds.dialogTestResult}
                  data-ok={tested.ok}
                  className={`text-xs ${tested.ok ? 'text-ok' : 'text-danger'}`}
                >
                  {tested.ok
                    ? `Connected: ${tested.models.length} ${tested.models.length === 1 ? 'model' : 'models'}`
                    : (tested.error ?? 'The connection failed.')}
                </span>
              )}
            </div>
            {tested?.ok === true && tested.models.length > 0 && (
              <ul className="m-0 max-h-24 list-none overflow-auto p-0 text-xs text-muted">
                {tested.models.map((m) => (
                  <li key={m}>{m}</li>
                ))}
              </ul>
            )}
          </div>
          <span className={label}>Default model</span>
          <ModelField
            testId={aiTestIds.dialogModel}
            label="Default model"
            models={tested?.ok === true ? tested.models : []}
            value={model}
            onChange={setModel}
          />
        </div>
        <div className="flex items-center justify-end gap-2 border-t border-border px-4 py-3">
          {error !== null && (
            <p
              role="alert"
              data-testid={aiTestIds.dialogError}
              className="m-0 mr-auto text-xs text-danger"
            >
              {error}
            </p>
          )}
          <button type="button" onClick={onClose} className={button}>
            Cancel
          </button>
          <button
            type="submit"
            data-testid={aiTestIds.dialogSave}
            disabled={busy}
            className={primaryButton}
          >
            Save
          </button>
        </div>
      </form>
    </dialog>
  );
}
