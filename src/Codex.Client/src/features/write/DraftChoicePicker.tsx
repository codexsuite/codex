import type { AiModelChoices } from '@codex/core/api/ai';
import type { Prompts } from '@codex/core/api/prompts';
import type { DraftChoice } from './draftStore';
import { ModelGroup } from './ModelGroup';
import { writeTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface DraftChoicePickerProps {
  prompts: Prompts;
  models: AiModelChoices;
  choice: DraftChoice;
  onChoose: (choice: DraftChoice) => void;
  onClose: () => void;
}

// Prompt templates beside the user's integrations and their models (one group each), as in the design: picking either one changes the choice and keeps the list open
// until the other is set or the picker is dismissed.
export function DraftChoicePicker({
  prompts,
  models,
  choice,
  onChoose,
  onClose,
}: DraftChoicePickerProps) {
  useReturnFocus();
  return (
    <div
      data-testid={writeTestIds.draftPicker}
      role="dialog"
      aria-label="Prompt template and model"
      className="write-draft-picker"
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <div role="radiogroup" aria-label="Prompt" className="flex flex-col gap-1">
        <span className="prose-card-label">Prompt</span>
        {prompts.templates.map((t) => (
          <button
            key={t.id}
            type="button"
            role="radio"
            aria-checked={choice.templateId === t.id}
            title={t.description}
            data-testid={writeTestIds.draftTemplate(t.id)}
            className="text-left"
            onClick={() => onChoose({ ...choice, templateId: t.id })}
          >
            {t.name}
          </button>
        ))}
      </div>
      {models.integrations.map((integration) => (
        <ModelGroup
          key={integration.integrationId}
          integration={integration}
          choice={choice}
          onPick={(model) => {
            onChoose({ ...choice, integrationId: integration.integrationId, model });
            onClose();
          }}
        />
      ))}
    </div>
  );
}
