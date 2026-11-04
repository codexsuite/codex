import { useState } from 'react';
import {
  deleteTemplate,
  duplicateTemplate,
  updateTemplate,
  type AiTemplate,
  type AiTemplates,
} from '@codex/core/api/ai';
import { ApiRequestError } from '@codex/core/api/client';
import { button, field, primaryButton } from '../auth/styles';
import { TemplateEditor } from './TemplateEditor';
import { aiTestIds } from './testIds';

const message = (e: unknown, fallback: string) =>
  e instanceof ApiRequestError ? e.message : fallback;

interface TemplatesPanelProps {
  templates: AiTemplates;
  onChanged: () => Promise<void>;
}

// The template list: built-in rows show "Built in" and only offer Duplicate; your own can also be edited and deleted.
export function TemplatesPanel({ templates, onChanged }: TemplatesPanelProps) {
  const [duplicating, setDuplicating] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<AiTemplate | null>(null);
  const small = 'h-7 rounded border border-border bg-transparent px-2 text-xs text-text';

  async function duplicate(source: AiTemplate) {
    setError(null);
    try {
      const copy = await duplicateTemplate(source.id, name);
      setDuplicating(null);
      setName('');
      await onChanged();
      setEditing(copy);
    } catch (e) {
      setError(message(e, 'Could not duplicate the template. Try again.'));
    }
  }

  async function remove(template: AiTemplate) {
    setError(null);
    try {
      await deleteTemplate(template.id);
      await onChanged();
    } catch (e) {
      setError(message(e, 'Could not delete the template. Try again.'));
    }
  }

  return (
    <div data-testid={aiTestIds.templates} className="flex flex-col gap-2">
      <ul className="m-0 flex list-none flex-col gap-1 p-0">
        {templates.templates.map((t) => (
          <li
            key={t.id}
            data-testid={aiTestIds.templateRow(t.id)}
            className="flex flex-col gap-2 rounded border border-border bg-bg px-3 py-2"
          >
            <div className="flex items-center gap-2">
              <span className="text-sm text-text">{t.name}</span>
              {t.builtIn && (
                <span
                  data-testid={aiTestIds.templateBuiltIn(t.id)}
                  className="rounded bg-surface px-1.5 text-xs text-muted"
                >
                  Built in
                </span>
              )}
              <span className="min-w-0 flex-1 truncate text-xs text-muted">{t.description}</span>
              {!t.builtIn && (
                <>
                  <button
                    type="button"
                    data-testid={aiTestIds.templateEdit(t.id)}
                    className={small}
                    onClick={() => setEditing(t)}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    data-testid={aiTestIds.templateDelete(t.id)}
                    className={small}
                    onClick={() => void remove(t)}
                  >
                    Delete
                  </button>
                </>
              )}
              <button
                type="button"
                data-testid={aiTestIds.templateDuplicate(t.id)}
                className={small}
                onClick={() => {
                  setDuplicating(t.id);
                  setName('');
                  setError(null);
                }}
              >
                Duplicate
              </button>
            </div>
            {duplicating === t.id && (
              <form
                className="flex items-center gap-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  void duplicate(t);
                }}
              >
                <input
                  data-testid={aiTestIds.duplicateName}
                  aria-label="Name for the copy"
                  required
                  maxLength={100}
                  autoFocus
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  className={`${field} flex-1`}
                />
                <button
                  type="submit"
                  data-testid={aiTestIds.duplicateSave}
                  className={primaryButton}
                >
                  Duplicate
                </button>
                <button type="button" className={button} onClick={() => setDuplicating(null)}>
                  Cancel
                </button>
                {error !== null && (
                  <span
                    role="alert"
                    data-testid={aiTestIds.duplicateError}
                    className="text-xs text-danger"
                  >
                    {error}
                  </span>
                )}
              </form>
            )}
          </li>
        ))}
      </ul>
      {error !== null && duplicating === null && (
        <p role="alert" className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
      {editing !== null && (
        <TemplateEditor
          key={editing.id}
          template={editing}
          variables={templates.variables}
          onCancel={() => setEditing(null)}
          onSave={async (request) => {
            await updateTemplate(editing.id, request);
            await onChanged();
            setEditing(null);
          }}
        />
      )}
    </div>
  );
}
