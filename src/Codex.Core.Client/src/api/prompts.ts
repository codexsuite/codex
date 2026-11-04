import { api } from './client';

// Codex.Contracts.Prompts.PromptsDto: the draft panel's template and model pickers.
export interface PromptTemplate {
  id: string;
  name: string;
  description: string;
  /** Ships with the app: read-only. Anything else is the signed-in user's own (Settings → AI). */
  builtIn: boolean;
}

export interface PromptModel {
  id: string;
  label: string;
  default: boolean;
}

export interface Prompts {
  templates: PromptTemplate[];
  models: PromptModel[];
}

export const getPrompts = (signal?: AbortSignal) => api.get<Prompts>('/api/prompts', { signal });
