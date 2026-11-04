import { useQuery, useQueryClient } from '@tanstack/react-query';
import { aiTemplatesKey, getAiTemplates } from '@codex/core/api/ai';
import { TemplatesPanel } from './TemplatesPanel';

// Settings → AI, Prompt templates (J22): the built-in templates (read-only, duplicable) beside your own. Which model a
// draft uses comes from your AI providers above; a template only decides what the model is asked.
export function PromptTemplatesSection() {
  const queryClient = useQueryClient();
  const templates = useQuery({
    queryKey: aiTemplatesKey,
    queryFn: ({ signal }) => getAiTemplates(signal),
  });

  // The Write draft picker reads /api/prompts: anything saved here changes it.
  const changed = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: aiTemplatesKey }),
      queryClient.invalidateQueries({ queryKey: ['prompts'] }),
    ]).then(() => undefined);

  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border bg-surface p-4">
      <h2 className="m-0 text-md font-semibold">Prompt templates</h2>
      <p className="m-0 text-sm text-muted">
        What the model is asked when you draft. Built-in templates cannot be changed; duplicate one
        to make your own.
      </p>
      {templates.isError && (
        <p role="alert" className="m-0 text-sm text-danger">
          Could not load your prompt templates.
        </p>
      )}
      {templates.data !== undefined && (
        <TemplatesPanel templates={templates.data} onChanged={changed} />
      )}
    </section>
  );
}
