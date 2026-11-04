import { useState } from 'react';
import type { AiTemplate, SaveTemplate } from '@codex/core/api/ai';
import { ApiRequestError } from '@codex/core/api/client';
import { button, field, primaryButton } from '../auth/styles';
import { aiTestIds } from './testIds';

/** About four characters to a token: the estimate for the template alone (the draft adds the beat and context). */
export const estimateTokens = (text: string) => Math.ceil(text.length / 4);

interface TemplateEditorProps {
  template: AiTemplate;
  variables: string[];
  onSave: (request: SaveTemplate) => Promise<void>;
  onCancel: () => void;
}

// Edits one of your own templates. An empty body cannot be saved; a body without {{beat}} can, with a warning.
export function TemplateEditor({ template, variables, onSave, onCancel }: TemplateEditorProps) {
  const [name, setName] = useState(template.name);
  const [body, setBody] = useState(template.body);
  const [error, setError] = useState<string | null>(null);
  const empty = body.trim() === '';

  async function save() {
    setError(null);
    try {
      await onSave({ name, description: template.description, body });
    } catch (e) {
      setError(
        e instanceof ApiRequestError ? e.message : 'Could not save the template. Try again.',
      );
    }
  }

  return (
    <form
      data-testid={aiTestIds.editor}
      className="flex flex-col gap-2 rounded border border-border bg-bg p-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (!empty) void save();
      }}
    >
      <input
        data-testid={aiTestIds.editorName}
        aria-label="Template name"
        required
        maxLength={100}
        value={name}
        onChange={(e) => setName(e.target.value)}
        className={field}
      />
      <textarea
        data-testid={aiTestIds.editorBody}
        aria-label="Prompt"
        rows={10}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        className={`${field} font-mono text-sm`}
      />
      <div className="flex flex-wrap items-center gap-1 text-xs text-muted">
        <span>Variables:</span>
        {variables.map((v) => (
          <button
            key={v}
            type="button"
            data-testid={aiTestIds.editorVariable(v)}
            className="rounded border border-border bg-transparent px-1.5 font-mono text-xs text-text"
            onClick={() => setBody((current) => `${current}{{${v}}}`)}
          >
            {`{{${v}}}`}
          </button>
        ))}
        <span className="flex-1" />
        <span data-testid={aiTestIds.editorTokens}>About {estimateTokens(body)} tokens</span>
      </div>
      {empty && (
        <p role="alert" data-testid={aiTestIds.editorError} className="m-0 text-xs text-danger">
          The prompt cannot be empty
        </p>
      )}
      {!empty && !body.includes('{{beat}}') && (
        <p role="status" data-testid={aiTestIds.editorWarning} className="m-0 text-xs text-warn">
          The model will not see the beat
        </p>
      )}
      {error !== null && (
        <p role="alert" data-testid={aiTestIds.editorError} className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
      <div className="flex gap-2">
        <button
          type="submit"
          disabled={empty}
          data-testid={aiTestIds.editorSave}
          className={primaryButton}
        >
          Save
        </button>
        <button
          type="button"
          data-testid={aiTestIds.editorCancel}
          className={button}
          onClick={onCancel}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
