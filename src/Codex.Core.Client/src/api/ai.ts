import { api } from './client';

// Per-user AI integrations (#36): Settings → AI and the Write draft panel's model picker. C#:
// Codex.Contracts.Ai.Integrations. The key never comes back from the server: keyHint is "sk-…abcd".

export type AiIntegrationKind = 'openai_compatible' | 'openrouter' | 'anthropic';

export interface AiIntegration {
  id: string;
  kind: AiIntegrationKind;
  label: string;
  /** openai_compatible only. */
  baseUrl: string | null;
  keyHint: string;
  defaultModel: string | null;
  /** From the last Test connection; empty means the author types a model id. */
  models: string[];
  isDefault: boolean;
  createdLabel: string;
  /** null: never used. */
  lastUsedLabel: string | null;
  lastError: string | null;
}

export interface AiSettings {
  integrations: AiIntegration[];
  /** The server owner allows http:// and private-network base URLs (CODEX_AI_ALLOW_LOCAL_ENDPOINTS). */
  localEndpointsAllowed: boolean;
}

export interface CreateAiIntegrationRequest {
  kind: AiIntegrationKind;
  label: string;
  baseUrl: string | null;
  apiKey: string;
  defaultModel: string | null;
}

/** Omitted members are left as they are; apiKey is write-only. */
export interface UpdateAiIntegrationRequest {
  label?: string;
  baseUrl?: string;
  apiKey?: string;
  defaultModel?: string;
}

export interface TestAiConnectionRequest {
  kind: AiIntegrationKind;
  baseUrl: string | null;
  apiKey: string;
}

export interface AiConnectionTest {
  ok: boolean;
  models: string[];
  error: string | null;
}

export interface AiModelChoice {
  integrationId: string;
  kind: AiIntegrationKind;
  label: string;
  isDefault: boolean;
  defaultModel: string | null;
  models: string[];
}

export interface AiModelChoices {
  /** Empty: nothing connected ("Connect an AI provider in Settings"). */
  integrations: AiModelChoice[];
}

export const getAiSettings = (signal?: AbortSignal) =>
  api.get<AiSettings>('/api/ai/integrations', { signal });

export const createAiIntegration = (request: CreateAiIntegrationRequest) =>
  api.post<AiIntegration>('/api/ai/integrations', request);

export const updateAiIntegration = (id: string, request: UpdateAiIntegrationRequest) =>
  api.patch<AiIntegration>(`/api/ai/integrations/${id}`, request);

export const deleteAiIntegration = (id: string) => api.del<void>(`/api/ai/integrations/${id}`);

export const makeDefaultAiIntegration = (id: string) =>
  api.post<AiSettings>(`/api/ai/integrations/${id}/default`);

export const testAiConnection = (request: TestAiConnectionRequest) =>
  api.post<AiConnectionTest>('/api/ai/integrations/test', request);

export const testAiIntegration = (id: string) =>
  api.post<AiConnectionTest>(`/api/ai/integrations/${id}/test`);

export const getAiModels = (signal?: AbortSignal) =>
  api.get<AiModelChoices>('/api/ai/models', { signal });

// Settings → AI, Prompt templates (J22): the built-in templates (read-only) and the signed-in user's own. C#:
// Codex.Contracts.Prompts (AiTemplatesDto, AiTemplateDto, SaveTemplateRequest).

export interface AiTemplate {
  id: string;
  name: string;
  description: string;
  /** A built-in template's body is the instruction text it sends; it cannot be edited. */
  body: string;
  builtIn: boolean;
}

export interface AiTemplates {
  templates: AiTemplate[];
  /** The names a template body may use as {{name}}. */
  variables: string[];
}

export interface SaveTemplate {
  name: string;
  description: string | null;
  body: string;
}

/** What a generation sent ("Show what was sent"), from the request stored when it ran. */
export interface DraftSent {
  template: string | null;
  model: string;
  /** The label of the AI integration that answered; null when none was recorded. */
  integration: string | null;
  context: string[];
  inputTokens: number | null;
  outputTokens: number | null;
  instruction: string;
  prompt: string;
}

export const aiTemplatesKey = ['ai', 'templates'] as const;

export const getAiTemplates = (signal?: AbortSignal) =>
  api.get<AiTemplates>('/api/ai/templates', { signal });

export const createTemplate = (request: SaveTemplate) =>
  api.post<AiTemplate>('/api/ai/templates', request);

export const updateTemplate = (id: string, request: SaveTemplate) =>
  api.put<AiTemplate>(`/api/ai/templates/${id}`, request);

export const deleteTemplate = (id: string) => api.del<void>(`/api/ai/templates/${id}`);

export const duplicateTemplate = (id: string, name: string) =>
  api.post<AiTemplate>(`/api/ai/templates/${id}/duplicate`, { name });

export const getDraftSent = (generationId: string, signal?: AbortSignal) =>
  api.get<DraftSent>(`/api/generations/${generationId}/sent`, { signal });
