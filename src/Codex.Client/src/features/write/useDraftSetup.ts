import { useQuery } from '@tanstack/react-query';
import { getAiModels, type AiModelChoice, type AiModelChoices } from '@codex/core/api/ai';
import { getPrompts, type Prompts } from '@codex/core/api/prompts';
import { aiModelsKey } from '../ai/aiQueries';
import { draftStore, useDraftChoice, type DraftChoice } from './draftStore';

export interface DraftSetup {
  prompts: Prompts | undefined;
  /** The user's integrations and their models; undefined while loading. */
  models: AiModelChoices | undefined;
  /** What the next Generate uses: the author's pick for this target, else the first template and the default model. */
  choice: DraftChoice | undefined;
  choose: (choice: DraftChoice) => void;
}

export const promptsQuery = {
  queryKey: ['prompts'],
  queryFn: ({ signal }: { signal: AbortSignal }) => getPrompts(signal),
} as const;

export const aiModelsQuery = {
  queryKey: aiModelsKey,
  queryFn: ({ signal }: { signal: AbortSignal }) => getAiModels(signal),
} as const;

/** The model an integration starts on: its default model, else its first listed one, else none (the author types one). */
export const startModel = (integration: AiModelChoice) =>
  integration.defaultModel ?? integration.models[0] ?? '';

/** The default integration (else the first) and its model; null when nothing is connected. */
export function defaultIntegration(models: AiModelChoices): AiModelChoice | null {
  return models.integrations.find((i) => i.isDefault) ?? models.integrations[0] ?? null;
}

export function defaultChoice(prompts: Prompts, models: AiModelChoices): DraftChoice | null {
  const integration = defaultIntegration(models);
  if (integration === null) return null;
  return {
    templateId: prompts.templates[0]?.id ?? null,
    integrationId: integration.integrationId,
    model: startModel(integration),
  };
}

/** The label shown for a model id: the prompts' suggestion label when it has one. */
export const modelLabelFor = (prompts: Prompts | undefined, model: string) =>
  prompts?.models.find((m) => m.id === model)?.label ?? model;

// The template and model for a beat (or a scene's continuation): templates from `GET /api/prompts`, integrations and
// their models from `GET /api/ai/models`. A pick whose integration has since been removed falls back to the default.
export function useDraftSetup(target: string): DraftSetup {
  const prompts = useQuery(promptsQuery).data;
  const models = useQuery(aiModelsQuery).data;
  const picked = useDraftChoice(target);
  const stillThere =
    picked !== undefined &&
    models?.integrations.some((i) => i.integrationId === picked.integrationId);
  const fallback = prompts && models ? defaultChoice(prompts, models) : null;
  return {
    prompts,
    models,
    choice: stillThere ? picked : (fallback ?? undefined),
    choose: (choice) => draftStore.setChoice(target, choice),
  };
}
