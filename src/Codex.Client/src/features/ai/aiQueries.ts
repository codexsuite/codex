import type { QueryClient } from '@tanstack/react-query';
import { getAiSettings, type AiIntegrationKind } from '@codex/core/api/ai';

export const aiSettingsKey = ['ai', 'settings'] as const;
export const aiModelsKey = ['ai', 'models'] as const;

export const aiSettingsQuery = {
  queryKey: aiSettingsKey,
  queryFn: ({ signal }: { signal: AbortSignal }) => getAiSettings(signal),
} as const;

export const kindLabels: Record<AiIntegrationKind, string> = {
  openai_compatible: 'OpenAI-compatible',
  openrouter: 'OpenRouter',
  anthropic: 'Anthropic',
};

/** Settings and the Write picker both read the integrations: after any change, refresh both. */
export const refreshAi = (queryClient: QueryClient) =>
  Promise.all([
    queryClient.invalidateQueries({ queryKey: aiSettingsKey }),
    queryClient.invalidateQueries({ queryKey: aiModelsKey }),
  ]);
