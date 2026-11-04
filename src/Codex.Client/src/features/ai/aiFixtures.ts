import type { AiModelChoices } from '@codex/core/api/ai';

// GET /api/ai/models as the component specs stub it: the default Anthropic integration with its models, and an
// OpenAI-compatible gateway that listed none (the author types a model id).
export const anthropicIntegrationId = '0199a000-0000-7000-8000-0000000000b1';
export const gatewayIntegrationId = '0199a000-0000-7000-8000-0000000000b2';

export const aiModelChoices: AiModelChoices = {
  integrations: [
    {
      integrationId: anthropicIntegrationId,
      kind: 'anthropic',
      label: 'Anthropic',
      isDefault: true,
      defaultModel: 'claude-sonnet-5-5',
      models: ['claude-sonnet-5-5', 'claude-opus-5-5', 'claude-haiku-5-5'],
    },
    {
      integrationId: gatewayIntegrationId,
      kind: 'openai_compatible',
      label: 'Gateway',
      isDefault: false,
      defaultModel: null,
      models: [],
    },
  ],
};
