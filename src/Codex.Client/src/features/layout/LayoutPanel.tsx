import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { ExportJob } from '@codex/core/api/exports';
import type {
  LayoutOption,
  LayoutOptionValue,
  LayoutPreview,
  LayoutState,
} from '@codex/core/api/layout';
import { writeLink } from '@codex/core/shell/links';
import { ExportsTab } from './ExportsTab';
import { PreflightTab } from './PreflightTab';
import type { SceneInfo } from './sceneLabels';
import { layoutTestIds } from './testIds';
import type { ExportSession } from './useExports';
import { useCanWrite } from '../../shell/bookAccess';

export type PanelTab = 'template' | 'issues' | 'preflight' | 'exports';

const TAB_TEXT: Record<PanelTab, string> = {
  template: 'Template',
  issues: 'Issues',
  preflight: 'Preflight',
  exports: 'Exports',
};

interface LayoutPanelProps {
  bookId: string;
  layout: LayoutState;
  preview: LayoutPreview | undefined;
  sceneInfo: (sceneId: string) => SceneInfo | undefined;
  onOption: (key: string, value: LayoutOptionValue) => void;
  onShow: (sceneId: string) => void;
  /** Changes with the template, trim and options, so Preflight follows them. */
  fingerprint: string;
  exports: ExportSession;
  onExportAgain: (job: ExportJob) => void;
  /** Asks the panel to show a tab (a new nonce each time), e.g. Exports once an export is queued. */
  openTab?: { tab: PanelTab; nonce: number };
}

const KIND_TEXT: Record<string, string> = {
  raw_latex: 'Raw LaTeX block',
  unknown_directive: 'Unknown directive',
  missing_asset: 'Image missing',
  missing_entry: 'Knowledge base entry missing',
  missing_reference: 'Reference missing',
};

// Template (the card, a notice for a missing preview.css, and the options as small segmented controls), Issues (what the
// preview found: low-dpi images and the flags), Preflight (the whole book, before Export for printer) and Exports (the
// running job and the history). The package files are not built yet.
export function LayoutPanel({
  bookId,
  layout,
  preview,
  sceneInfo,
  onOption,
  onShow,
  fingerprint,
  exports,
  onExportAgain,
  openTab,
}: LayoutPanelProps) {
  const [tab, setTab] = useState<PanelTab>('template');
  // Below write (#80) the template's options show, but do not change.
  const canWrite = useCanWrite();
  const [seenNonce, setSeenNonce] = useState(openTab?.nonce);
  if (openTab && openTab.nonce !== seenNonce) {
    setSeenNonce(openTab.nonce);
    setTab(openTab.tab);
  }
  const template = layout.templates.find((t) => t.slug === layout.template);
  const low = (preview?.figures ?? []).filter((f) => f.status === 'low');
  const flags = preview?.flags ?? [];
  const count = low.length + flags.length;
  return (
    <aside
      data-testid={layoutTestIds.panel}
      className="w-80 shrink-0 overflow-auto border-l border-border bg-surface text-sm"
    >
      <div className="flex border-b border-border" role="tablist">
        {(['template', 'issues', 'preflight', 'exports'] as const).map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={tab === name}
            data-testid={layoutTestIds.tab(name)}
            className={`flex-1 px-3 py-2 text-xs ${tab === name ? 'border-b-2 border-accent font-semibold' : 'text-muted'}`}
            onClick={() => setTab(name)}
          >
            {name === 'issues' && count > 0 ? `Issues (${count})` : TAB_TEXT[name]}
          </button>
        ))}
      </div>
      {tab === 'template' && template && (
        <div className="p-3">
          <p className="m-0 font-semibold">{template.name}</p>
          <p className="m-0 text-xs text-muted">
            {template.engine} · v{template.version} · {template.columns} column
            {template.columns === 1 ? '' : 's'}
          </p>
          <fieldset disabled={!canWrite} className="m-0 min-w-0 border-0 p-0">
            {template.options.map((option) => (
              <OptionControl
                key={option.key}
                option={option}
                onChange={(v) => onOption(option.key, v)}
              />
            ))}
          </fieldset>
        </div>
      )}
      {tab === 'preflight' && (
        <PreflightTab bookId={bookId} fingerprint={fingerprint} sceneInfo={sceneInfo} />
      )}
      {tab === 'exports' && <ExportsTab session={exports} onAgain={onExportAgain} />}
      {tab === 'issues' && (
        <ul data-testid={layoutTestIds.issuesList} className="m-0 list-none p-3">
          {count === 0 && <li className="text-muted">No print issues in this preview.</li>}
          {low.map((figure) => (
            <li key={figure.figureId} data-testid={layoutTestIds.issueItem} className="mb-2">
              <span className="text-warn">Low resolution</span> · {figure.filename} ·{' '}
              {figure.effectiveDpi} dpi
              <IssueLinks
                bookId={bookId}
                sceneId={figure.sceneId}
                sceneInfo={sceneInfo}
                onShow={onShow}
              />
            </li>
          ))}
          {flags.map((flag, i) => (
            <li key={i} data-testid={layoutTestIds.issueItem} className="mb-2">
              <span className="text-warn">{KIND_TEXT[flag.kind] ?? flag.kind}</span> · {flag.detail}
              {flag.sceneId !== null && (
                <IssueLinks
                  bookId={bookId}
                  sceneId={flag.sceneId}
                  sceneInfo={sceneInfo}
                  onShow={onShow}
                />
              )}
            </li>
          ))}
        </ul>
      )}
    </aside>
  );
}

function IssueLinks(props: {
  bookId: string;
  sceneId: string;
  sceneInfo: (sceneId: string) => SceneInfo | undefined;
  onShow: (id: string) => void;
}) {
  const scene = props.sceneInfo(props.sceneId);
  return (
    <div className="flex gap-3 text-xs">
      {scene && <span className="text-muted">{scene.label}</span>}
      <button
        type="button"
        data-testid={layoutTestIds.showOnPage}
        className="text-accent"
        onClick={() => props.onShow(props.sceneId)}
      >
        Show on page
      </button>
      {scene && (
        <Link className="text-accent" to={writeLink(props.bookId, { scene: scene.number })}>
          Open in Write
        </Link>
      )}
    </div>
  );
}

function OptionControl({
  option,
  onChange,
}: {
  option: LayoutOption;
  onChange: (value: LayoutOptionValue) => void;
}) {
  const choices: LayoutOptionValue[] = option.type === 'boolean' ? [false, true] : option.choices;
  return (
    <div className="mt-3" data-testid={layoutTestIds.option(option.key)}>
      <div className="mb-1 text-xs text-muted">{option.label}</div>
      <div className="flex" role="group" aria-label={option.label}>
        {choices.map((choice) => (
          <button
            key={String(choice)}
            type="button"
            aria-pressed={option.value === choice}
            onClick={() => onChange(choice)}
            className={`border border-border px-2 py-1 text-xs first:rounded-l-md last:rounded-r-md ${option.value === choice ? 'bg-accent text-accent-fg' : ''}`}
          >
            {typeof choice === 'boolean' ? (choice ? 'On' : 'Off') : choice}
          </button>
        ))}
      </div>
    </div>
  );
}
