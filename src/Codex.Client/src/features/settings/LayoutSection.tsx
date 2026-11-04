import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import {
  layoutApi,
  layoutKey,
  type LayoutOptionValue,
  type LayoutSelection,
  type LayoutState,
} from '@codex/core/api/layout';
import { layoutTemplatesApi } from '@codex/core/api/layoutTemplates';
import { layoutSettingsKey, type LayoutTemplate } from '@codex/core/api/settings';
import { useLayoutSettings } from '../../api/settingsQueries';
import { layoutLink } from '@codex/core/shell/links';
import { ErrorState } from '../../shell/ErrorState';
import { LayoutSectionGate } from './LayoutSectionGate';
import { LayoutSectionLibrary } from './LayoutSectionLibrary';
import { LayoutSectionOptions } from './LayoutSectionOptions';
import { LayoutSectionTemplate } from './LayoutSectionTemplate';
import { LayoutSectionTrims } from './LayoutSectionTrims';
import { LayoutSectionUpgrade } from './LayoutSectionUpgrade';
import { SettingsCard } from './SettingsCard';
import { settingsTestIds } from './testIds';

// What a book that has not stored a choice uses: the default template and its first trim, as Layout resolves them.
function resolved(state: LayoutState): LayoutTemplate | null {
  const template = state.templates.find((t) => t.slug === state.template);
  if (!template) return null;
  return {
    name: template.name,
    description: null,
    isUserTemplate: template.isUserTemplate ?? false,
    pinnedVersion: template.version,
    latestVersion: null,
    trimSize: state.trim,
    trims: template.trims,
  };
}

// Layout & print: the template with its pinned version, the upgrade to a newer version (changelog, Preview, Apply), the
// trim size and the template's options, all changeable here (the Layout view's own choice, `PUT /api/books/{id}/layout`);
// the template library (built-ins and the user's own, with thumbnails, Fork, Upload, Download, Archive); and the
// print-export gate. Until Layout's state has loaded the template and trim are shown only.
export function LayoutSection({ bookId }: { bookId: string }) {
  const queryClient = useQueryClient();
  const layout = useLayoutSettings(bookId);
  const state = useQuery({
    queryKey: layoutKey(bookId),
    queryFn: ({ signal }) => layoutApi.state(bookId, { signal }),
    retry: false,
  });
  const select = useMutation({
    mutationFn: (selection: LayoutSelection) => layoutApi.select(bookId, selection),
    onSuccess: (next) => {
      queryClient.setQueryData(layoutKey(bookId), next);
      void queryClient.invalidateQueries({ queryKey: layoutSettingsKey(bookId) });
    },
  });
  const shown = layout.data?.template ?? (state.data ? resolved(state.data) : null);
  const current = state.data?.templates.find((t) => t.slug === state.data?.template);
  const choose = (change: Partial<LayoutSelection>) => {
    if (state.data) select.mutate({ template: state.data.template, ...change });
  };
  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: layoutKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: layoutSettingsKey(bookId) }),
    ]);
  };
  const change = async (run: () => Promise<unknown>) => {
    await run();
    await refresh();
  };
  const option = (key: string, value: LayoutOptionValue) => choose({ options: { [key]: value } });
  return (
    <SettingsCard
      section="layout"
      title="Layout & print"
      blurb="The template the Layout view, proofs and Export for printer use. Options, preflight and exports are on Layout."
    >
      <div className="flex justify-end pt-2">
        <Link to={layoutLink(bookId)} className="text-xs text-accent">
          Layout
        </Link>
      </div>
      {layout.isError && (
        <ErrorState
          testId={settingsTestIds.layoutError}
          title="Layout could not be loaded"
          detail={layout.error.message}
        />
      )}
      {layout.data &&
        (shown ? (
          <>
            <LayoutSectionTemplate
              template={shown}
              choices={
                state.data && {
                  current: state.data.template,
                  templates: state.data.templates,
                  onPick: (slug) => choose({ template: slug, trim: undefined }),
                }
              }
            >
              {current && (
                <LayoutSectionUpgrade
                  bookId={bookId}
                  template={current}
                  onApply={(versionId) =>
                    change(() => layoutTemplatesApi.upgrade(bookId, versionId))
                  }
                />
              )}
            </LayoutSectionTemplate>
            <LayoutSectionTrims
              template={shown}
              onTrim={state.data ? (trim) => choose({ trim }) : undefined}
            />
            {current && <LayoutSectionOptions template={current} onOption={option} />}
          </>
        ) : (
          <p data-testid={settingsTestIds.layoutNone} className="m-0 py-3 text-sm text-muted">
            No template chosen yet. Pick one in Layout.
          </p>
        ))}
      {layout.data && state.data && (
        <LayoutSectionLibrary
          current={state.data.template}
          templates={state.data.templates}
          onUse={(slug) => choose({ template: slug, trim: undefined })}
          onChange={change}
        />
      )}
      {layout.data && <LayoutSectionGate bookId={bookId} riskCount={layout.data.riskCount} />}
    </SettingsCard>
  );
}
