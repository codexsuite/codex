import type { ReactNode } from 'react';
import type { LayoutTemplate } from '@codex/core/api/settings';
import { FieldRow } from './FieldRow';
import { settingsTestIds } from './testIds';

export interface TemplateChoices {
  /** Slug of the template the book uses. */
  current: string;
  templates: { slug: string; name: string; version: string; isUserTemplate?: boolean }[];
  onPick: (slug: string) => void;
}

// The template the book uses now, pinned to a version, and (once Layout is loaded) a picker for another and, under it,
// the upgrade to a newer version (children).
export function LayoutSectionTemplate({
  template,
  choices,
  children,
}: {
  template: LayoutTemplate;
  choices?: TemplateChoices;
  children?: ReactNode;
}) {
  return (
    <FieldRow label="Template">
      <div data-testid={settingsTestIds.layoutTemplate} className="flex flex-col gap-0.5">
        <b className="text-base text-text">
          {template.name}
          {template.isUserTemplate ? ' (yours)' : ''}
        </b>
        {template.description && (
          <span data-testid={settingsTestIds.layoutDescription} className="text-sm text-muted">
            {template.description}
          </span>
        )}
        <span data-testid={settingsTestIds.layoutVersion} className="text-xs text-muted">
          Pinned to v{template.pinnedVersion}
        </span>
        {template.latestVersion && (
          <span data-testid={settingsTestIds.layoutUpgrade} className="text-xs text-warn">
            v{template.latestVersion} available
          </span>
        )}
        {choices && (
          <select
            aria-label="Template"
            data-testid={settingsTestIds.layoutTemplatePick}
            value={choices.current}
            onChange={(e) => choices.onPick(e.target.value)}
            className="mt-1 h-8 w-fit rounded border border-border bg-surface px-2 text-base text-text"
          >
            {choices.templates.map((t) => (
              <option key={t.slug} value={t.slug}>
                {t.name}
                {t.isUserTemplate ? ' (yours)' : ''} v{t.version}
              </option>
            ))}
          </select>
        )}
        {children}
      </div>
    </FieldRow>
  );
}
