import type { LayoutState } from '@codex/core/api/layout';
import { layoutTestIds } from './testIds';
import type { CanvasView } from './PageCanvas';
import type { ScopeOption } from './sceneLabels';
import { useCanWrite } from '../../shell/bookAccess';

const SELECT = 'rounded-md border border-border bg-surface px-2 py-1 text-xs';
const SEGMENT = 'border border-border px-2 py-1 text-xs first:rounded-l-md last:rounded-r-md';

export type LayoutMode = 'quick' | 'proof';

interface LayoutToolbarProps {
  scopes: ScopeOption[];
  scope: string;
  onScope: (scope: string) => void;
  layout: LayoutState;
  mode: LayoutMode;
  onMode: (mode: LayoutMode) => void;
  /** Proof covers one chapter: off when the scope is the whole book. */
  canProof: boolean;
  onTemplate: (slug: string) => void;
  onTrim: (trim: string) => void;
  view: CanvasView;
  onView: (view: CanvasView) => void;
  zoom: number;
  onZoom: (zoom: number) => void;
  showIssues: boolean;
  onShowIssues: (show: boolean) => void;
  onExport: () => void;
}

// What to preview, Quick or Proof, the template and trim, then how the pages are shown. Quick is the browser's pages, Proof the worker's PDF.
export function LayoutToolbar(props: LayoutToolbarProps) {
  const { layout } = props;
  const template = layout.templates.find((t) => t.slug === layout.template);
  // Below write (#80) the preview works, but the template and trim are the book's settings, and export and proof need
  // write.
  const canWrite = useCanWrite();
  return (
    <div
      data-testid={layoutTestIds.toolbar}
      className="flex flex-wrap items-center gap-2 border-b border-border bg-surface px-3 py-2"
    >
      <select
        aria-label="What to preview"
        data-testid={layoutTestIds.scopeSelect}
        className={SELECT}
        value={props.scope}
        onChange={(e) => props.onScope(e.target.value)}
      >
        {props.scopes.map((o) => (
          <option key={o.value} value={o.value}>
            {o.indent ? `· ${o.label}` : o.label}
          </option>
        ))}
      </select>
      <div className="flex" role="group" aria-label="Preview kind">
        {(['quick', 'proof'] as const).map((mode) => (
          <button
            key={mode}
            type="button"
            aria-pressed={props.mode === mode}
            disabled={mode === 'proof' && (!canWrite || !props.canProof)}
            title={
              mode !== 'proof'
                ? undefined
                : !canWrite
                  ? 'A proof needs edit access to the book'
                  : !props.canProof
                    ? 'A proof is one chapter: pick a chapter or a scene'
                    : undefined
            }
            data-testid={mode === 'quick' ? layoutTestIds.modeQuick : layoutTestIds.modeProof}
            className={`${SEGMENT} ${props.mode === mode ? 'bg-accent text-accent-fg' : ''} disabled:text-muted`}
            onClick={() => props.onMode(mode)}
          >
            {mode === 'quick' ? 'Quick' : 'Proof'}
          </button>
        ))}
      </div>
      <select
        aria-label="Template"
        data-testid={layoutTestIds.templateSelect}
        disabled={!canWrite}
        className={SELECT}
        value={layout.template}
        onChange={(e) => props.onTemplate(e.target.value)}
      >
        {layout.templates.map((t) => (
          <option key={t.slug} value={t.slug}>
            {t.name} v{t.version}
          </option>
        ))}
      </select>
      <select
        aria-label="Trim size"
        data-testid={layoutTestIds.trimSelect}
        disabled={!canWrite}
        className={SELECT}
        value={layout.trim}
        onChange={(e) => props.onTrim(e.target.value)}
      >
        {(template?.trims ?? []).map((trim) => (
          <option key={trim} value={trim}>
            {trim}
          </option>
        ))}
      </select>
      <span className="grow" />
      {canWrite && (
        <button
          type="button"
          data-testid={layoutTestIds.exportOpen}
          className="rounded-md border-0 bg-accent px-3 py-1 text-xs font-semibold text-accent-fg"
          onClick={props.onExport}
        >
          Export for printer…
        </button>
      )}
      <label className="flex items-center gap-1 text-xs">
        <input
          type="checkbox"
          data-testid={layoutTestIds.issuesToggle}
          checked={props.showIssues}
          onChange={(e) => props.onShowIssues(e.target.checked)}
        />
        Print issues
      </label>
      <div className="flex" role="group" aria-label="Page view">
        {(['single', 'spread'] as const).map((view) => (
          <button
            key={view}
            type="button"
            aria-pressed={props.view === view}
            data-testid={view === 'single' ? layoutTestIds.viewSingle : layoutTestIds.viewSpread}
            className={`${SEGMENT} ${props.view === view ? 'bg-accent text-accent-fg' : ''}`}
            onClick={() => props.onView(view)}
          >
            {view === 'single' ? 'Single' : 'Spread'}
          </button>
        ))}
      </div>
      <select
        aria-label="Zoom"
        data-testid={layoutTestIds.zoomSelect}
        className={SELECT}
        value={props.zoom}
        onChange={(e) => props.onZoom(Number(e.target.value))}
      >
        {[0.5, 0.75, 1, 1.25].map((z) => (
          <option key={z} value={z}>
            {Math.round(z * 100)}%
          </option>
        ))}
      </select>
    </div>
  );
}
