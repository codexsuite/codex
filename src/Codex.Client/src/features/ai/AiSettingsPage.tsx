import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { AuthPageBar } from '../auth/AuthPageBar';
import { primaryButton } from '../auth/styles';
import { aiSettingsQuery } from './aiQueries';
import { ConnectDialog } from './ConnectDialog';
import { IntegrationRow } from './IntegrationRow';
import { PromptTemplatesSection } from './PromptTemplatesSection';
import { aiTestIds } from './testIds';

// /settings/ai: the AI providers you have connected (#36), then your prompt templates (J22). They are yours, not a
// book's, so this sits beside Account. One request feeds each section (GET /api/ai/integrations, GET /api/ai/templates).
export function AiSettingsPage() {
  const settings = useQuery(aiSettingsQuery);
  const [connecting, setConnecting] = useState(false);
  const integrations = settings.data?.integrations;

  return (
    <div data-testid={aiTestIds.page} className="flex min-h-screen flex-col">
      <AuthPageBar title="AI" />
      <div className="mx-auto flex w-[min(720px,100%)] flex-col gap-4 p-4">
        <section className="flex flex-col gap-3 rounded-lg border border-border bg-surface p-4">
          <div className="flex items-center gap-2">
            <h2 className="m-0 flex-1 text-md font-semibold">AI providers</h2>
            <button
              type="button"
              data-testid={aiTestIds.connect}
              disabled={settings.data === undefined}
              className={primaryButton}
              onClick={() => setConnecting(true)}
            >
              Connect
            </button>
          </div>
          <p className="m-0 text-sm text-muted">
            Codex drafts prose with a provider you connect. Your keys are stored encrypted and are
            never shown again.
          </p>
          {settings.isError && (
            <p role="alert" data-testid={aiTestIds.error} className="m-0 text-sm text-danger">
              Could not load your AI providers. Reload the page to try again.
            </p>
          )}
          {integrations?.length === 0 && (
            <p data-testid={aiTestIds.empty} className="m-0 text-sm text-muted">
              No AI provider connected. Connect one to draft prose in Write.
            </p>
          )}
          {integrations !== undefined && integrations.length > 0 && (
            <ul data-testid={aiTestIds.list} className="m-0 flex list-none flex-col p-0">
              {integrations.map((integration) => (
                <IntegrationRow key={integration.id} integration={integration} />
              ))}
            </ul>
          )}
        </section>
        <PromptTemplatesSection />
      </div>
      {connecting && settings.data !== undefined && (
        <ConnectDialog
          localEndpointsAllowed={settings.data.localEndpointsAllowed}
          onClose={() => setConnecting(false)}
        />
      )}
    </div>
  );
}
