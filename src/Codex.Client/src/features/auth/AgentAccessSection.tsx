import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import {
  createToken,
  listTokens,
  revokeToken,
  tokensKey,
  type ApiTokenScope,
  type CreatedApiToken,
} from '@codex/core/api/auth';
import { ApiRequestError } from '@codex/core/api/client';
import { button, field, label, primaryButton } from './styles';
import { authTestIds } from './testIds';

const scopeChoices: { scope: ApiTokenScope; text: string }[] = [
  { scope: 'codex:read', text: 'Read your books' },
  { scope: 'codex:write', text: 'Edit outlines, prose and the Knowledge base' },
  { scope: 'codex:references', text: 'Change sources, uses and permission letters' },
  { scope: 'codex:generate', text: 'Ask Codex for drafts (spends your model budget)' },
];

const scopeNames: Record<ApiTokenScope, string> = {
  'codex:read': 'read',
  'codex:write': 'write',
  'codex:references': 'references',
  'codex:generate': 'generate',
};

const expiryChoices: { days: number | null; text: string }[] = [
  { days: 30, text: '30 days' },
  { days: 90, text: '90 days' },
  { days: 365, text: '1 year' },
  { days: null, text: 'Never' },
];

// Shown once after Create: the server keeps only a hash of the token.
function NewTokenBox({ created, onDismiss }: { created: CreatedApiToken; onDismiss: () => void }) {
  const [copied, setCopied] = useState(false);
  const command = `claude mcp add --transport http codex ${window.location.origin}/mcp --header "Authorization: Bearer ${created.secret}"`;
  return (
    <div role="status" className="flex flex-col gap-2 rounded-lg border border-border bg-bg p-3">
      <p className="m-0 text-sm text-text">
        Your new token for <strong className="font-semibold">{created.token.name}</strong>. Copy it
        now: it is shown only once.
      </p>
      <div className="flex items-center gap-2">
        <code
          data-testid={authTestIds.tokenSecret}
          className="min-w-0 flex-1 truncate rounded border border-border bg-surface px-2 py-1 font-mono text-sm text-text"
        >
          {created.secret}
        </code>
        <button
          type="button"
          data-testid={authTestIds.tokenSecretCopy}
          className={button}
          onClick={() => {
            void navigator.clipboard.writeText(created.secret).then(() => setCopied(true));
          }}
        >
          {copied ? 'Copied' : 'Copy'}
        </button>
        <button
          type="button"
          data-testid={authTestIds.tokenSecretDismiss}
          className={button}
          onClick={onDismiss}
        >
          Done
        </button>
      </div>
      <p className="m-0 text-xs text-muted">For Claude Code:</p>
      <code className="block break-all rounded border border-border bg-surface px-2 py-1 font-mono text-xs text-text">
        {command}
      </code>
    </div>
  );
}

function CreateTokenForm({ onCreated }: { onCreated: (created: CreatedApiToken) => void }) {
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<ApiTokenScope[]>(['codex:read']);
  const [days, setDays] = useState<number | null>(90);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onCreated(await createToken(name, scopes, days));
      setName('');
      setScopes(['codex:read']);
      setDays(90);
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'Could not create the token. Try again.');
    }
  }

  function toggle(scope: ApiTokenScope, on: boolean) {
    setScopes((current) => (on ? [...current, scope] : current.filter((s) => s !== scope)));
  }

  return (
    <form onSubmit={submit} className="grid grid-cols-[150px_1fr] items-start gap-x-3 gap-y-2">
      <label htmlFor="token-name" className={label}>
        Name
      </label>
      <input
        id="token-name"
        data-testid={authTestIds.tokenName}
        required
        maxLength={100}
        placeholder="Claude Code on the desktop"
        value={name}
        onChange={(e) => setName(e.target.value)}
        className={field}
      />
      <span className={label}>Can</span>
      <div className="flex flex-col gap-1">
        {scopeChoices.map(({ scope, text }) => (
          <label key={scope} className="flex items-center gap-2 text-sm text-text">
            <input
              type="checkbox"
              data-testid={authTestIds.tokenScope(scope)}
              checked={scopes.includes(scope)}
              disabled={scope === 'codex:read'}
              onChange={(e) => toggle(scope, e.target.checked)}
            />
            {text}
          </label>
        ))}
      </div>
      <label htmlFor="token-expiry" className={label}>
        Expires after
      </label>
      <select
        id="token-expiry"
        data-testid={authTestIds.tokenExpiry}
        value={days ?? 'never'}
        onChange={(e) => setDays(e.target.value === 'never' ? null : Number(e.target.value))}
        className={`${field} w-40`}
      >
        {expiryChoices.map(({ days: value, text }) => (
          <option key={text} value={value ?? 'never'}>
            {text}
          </option>
        ))}
      </select>
      <span />
      <div className="flex items-center gap-2">
        <button type="submit" data-testid={authTestIds.tokenCreate} className={primaryButton}>
          Create token
        </button>
        {error !== null && (
          <p role="alert" data-testid={authTestIds.tokensError} className="m-0 text-xs text-danger">
            {error}
          </p>
        )}
      </div>
    </form>
  );
}

// Account → Agent access (#32): personal access tokens an MCP client such as Claude Code sends to /mcp. Create one with
// a name, scopes and an expiry, see it once, and revoke any of them.
export function AgentAccessSection() {
  const queryClient = useQueryClient();
  const tokens = useQuery({ queryKey: tokensKey, queryFn: ({ signal }) => listTokens(signal) });
  const [created, setCreated] = useState<CreatedApiToken | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cell = 'border-b border-border px-2 py-1.5 text-sm';

  async function revoke(id: string) {
    setError(null);
    try {
      await revokeToken(id);
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'Could not revoke the token. Try again.');
    }
    await queryClient.invalidateQueries({ queryKey: tokensKey });
  }

  return (
    <section
      data-testid={authTestIds.tokensSection}
      className="flex flex-col gap-3 rounded-lg border border-border bg-surface p-4"
    >
      <h2 className="m-0 text-md font-semibold">Agent access</h2>
      <p className="m-0 text-sm text-muted">
        Personal access tokens let an agent such as Claude Code work on your books through
        Codex&apos;s MCP server. A token acts as you, with only the access you give it.
      </p>
      <CreateTokenForm
        onCreated={(next) => {
          setCreated(next);
          void queryClient.invalidateQueries({ queryKey: tokensKey });
        }}
      />
      {created !== null && <NewTokenBox created={created} onDismiss={() => setCreated(null)} />}
      {error !== null && (
        <p role="alert" data-testid={authTestIds.tokensError} className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
      {tokens.data?.length === 0 && (
        <p data-testid={authTestIds.tokensEmpty} className="m-0 text-sm text-muted">
          No tokens yet.
        </p>
      )}
      {tokens.data !== undefined && tokens.data.length > 0 && (
        <table className="w-full border-collapse text-left">
          <thead>
            <tr className="text-xs text-muted">
              <th className={`${cell} font-medium`}>Name</th>
              <th className={`${cell} font-medium`}>Token</th>
              <th className={`${cell} font-medium`}>Can</th>
              <th className={`${cell} font-medium`}>Last used</th>
              <th className={`${cell} font-medium`}>Expires</th>
              <th className={`${cell} font-medium`} aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {tokens.data.map((token) => (
              <tr key={token.id} data-testid={authTestIds.tokenRow(token.id)}>
                <td className={`${cell} text-text`}>{token.name}</td>
                <td className={`${cell} font-mono text-xs text-muted`}>{token.prefix}…</td>
                <td className={`${cell} text-text`}>
                  {token.scopes.map((s) => scopeNames[s]).join(', ')}
                </td>
                <td className={`${cell} text-muted`}>{token.lastUsedLabel ?? 'Never'}</td>
                <td className={`${cell} ${token.expired ? 'text-warn' : 'text-muted'}`}>
                  {token.expired
                    ? `Expired ${token.expiresLabel}`
                    : (token.expiresLabel ?? 'Never')}
                </td>
                <td className={`${cell} text-right`}>
                  <button
                    type="button"
                    data-testid={authTestIds.tokenRevoke(token.id)}
                    className="h-7 rounded border border-border bg-transparent px-2 text-xs text-text"
                    onClick={() => void revoke(token.id)}
                  >
                    Revoke
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
