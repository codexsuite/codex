import { useState } from 'react';
import type { AiModelChoice } from '@codex/core/api/ai';
import type { DraftChoice } from './draftStore';
import { writeTestIds } from './testIds';

interface ModelGroupProps {
  integration: AiModelChoice;
  choice: DraftChoice;
  onPick: (model: string) => void;
}

// One integration's models in the draft picker. An integration that listed none (a provider with no /models, or one
// never tested) takes a model id typed by the author.
export function ModelGroup({ integration, choice, onPick }: ModelGroupProps) {
  const { integrationId } = integration;
  const [typed, setTyped] = useState('');
  const models =
    integration.models.length > 0
      ? integration.models
      : integration.defaultModel !== null
        ? [integration.defaultModel]
        : [];
  const picked = (model: string) =>
    choice.integrationId === integrationId && choice.model === model;
  return (
    <div
      role="radiogroup"
      aria-label={`Model: ${integration.label}`}
      data-testid={writeTestIds.draftIntegration(integrationId)}
      className="flex flex-col gap-1"
    >
      <span className="prose-card-label">
        {integration.label}
        {integration.isDefault ? ' (default)' : ''}
      </span>
      {models.map((model) => (
        <button
          key={model}
          type="button"
          role="radio"
          aria-checked={picked(model)}
          data-testid={writeTestIds.draftModel(integrationId, model)}
          className="text-left"
          onClick={() => onPick(model)}
        >
          {model}
        </button>
      ))}
      {integration.models.length === 0 && (
        <span className="flex gap-1">
          <input
            data-testid={writeTestIds.draftModelText(integrationId)}
            aria-label={`Model id for ${integration.label}`}
            placeholder="Model id"
            autoComplete="off"
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && typed.trim() !== '') onPick(typed.trim());
            }}
            className="min-w-0 flex-1"
          />
          <button
            type="button"
            data-testid={writeTestIds.draftModelUse(integrationId)}
            disabled={typed.trim() === ''}
            onClick={() => onPick(typed.trim())}
          >
            Use
          </button>
        </span>
      )}
    </div>
  );
}
