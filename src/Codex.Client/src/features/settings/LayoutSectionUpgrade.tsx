import { useState } from 'react';
import type { LayoutTemplate } from '@codex/core/api/layout';
import { UpgradePreviewDialog } from './UpgradePreviewDialog';
import { settingsTestIds } from './testIds';

interface LayoutSectionUpgradeProps {
  bookId: string;
  template: LayoutTemplate;
  onApply: (versionId: string) => Promise<unknown>;
}

// Under the template when a newer published version exists: what changed since the pinned one, Preview (Quick preview
// with the newer version, nothing applied) and Apply (re-pins this book; other books keep their version).
export function LayoutSectionUpgrade({ bookId, template, onApply }: LayoutSectionUpgradeProps) {
  const [previewing, setPreviewing] = useState(false);
  const [applying, setApplying] = useState(false);
  const versionId = template.latestVersionId;
  const version = template.latestVersion;
  if (!versionId || !version) return null;

  const apply = async () => {
    setApplying(true);
    try {
      await onApply(versionId);
      setPreviewing(false);
    } finally {
      setApplying(false);
    }
  };

  return (
    <div className="flex basis-full flex-col gap-1.5 rounded-md border border-border bg-bg px-2.5 py-2">
      <span className="text-sm text-text">
        v{version} is available. This book uses v{template.version}.
      </span>
      {(template.latestChangelog?.length ?? 0) > 0 && (
        <ul data-testid={settingsTestIds.layoutChangelog} className="m-0 pl-4 text-xs text-muted">
          {template.latestChangelog?.map((line, i) => (
            <li key={i}>{line}</li>
          ))}
        </ul>
      )}
      <div className="flex gap-1.5">
        <button
          type="button"
          data-testid={settingsTestIds.layoutUpgradePreview}
          onClick={() => setPreviewing(true)}
          className="h-7 cursor-pointer rounded border border-border bg-surface px-2.5 text-sm text-text"
        >
          Preview v{version}
        </button>
        <button
          type="button"
          data-testid={settingsTestIds.layoutUpgradeApply}
          disabled={applying}
          onClick={() => void apply()}
          className="h-7 cursor-pointer rounded border-0 bg-accent px-2.5 text-sm text-white disabled:opacity-50"
        >
          Apply v{version}
        </button>
      </div>
      {previewing && (
        <UpgradePreviewDialog
          bookId={bookId}
          template={template}
          versionId={versionId}
          version={version}
          applying={applying}
          onClose={() => setPreviewing(false)}
          onApply={() => void apply()}
        />
      )}
    </div>
  );
}
