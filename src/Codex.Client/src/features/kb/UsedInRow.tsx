import { Link } from 'react-router-dom';
import type { KbUse } from '@codex/core/api/kbEntries';
import { writeLink } from '@codex/core/shell/links';
import { kindLabel } from '@codex/core/features/kb/kindLabels';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface UsedInRowProps {
  use: KbUse;
  onMakeMention: (use: KbUse) => void;
  onUnlink: (use: KbUse) => void;
}

// One scene that touches the entry: how, the sentence that shows it, Make mention (when the name is plain text),
// Unlink (when the scene is linked) and a link to it in Write.
export function UsedInRow({ use, onMakeMention, onUnlink }: UsedInRowProps) {
  const kinds = use.kinds.map((k) => k.kind);
  // Below write (#80) a use only links to Write.
  const canWrite = useCanWrite();
  return (
    <li data-testid={kbTestIds.useRow(use.sceneId)} className="border-b border-border px-4 py-2">
      <div className="flex items-center gap-2">
        <span className="text-sm font-medium">Scene {use.number}</span>
        <span className="flex flex-1 flex-wrap gap-1">
          {use.kinds.map((k) => (
            <span
              key={k.kind}
              data-testid={kbTestIds.useKind(use.sceneId, k.kind)}
              className="rounded bg-accent-soft px-1.5 text-2xs text-text"
            >
              {kindLabel(k.kind)}
            </span>
          ))}
        </span>
        {canWrite && kinds.includes('text') && (
          <button
            type="button"
            data-testid={kbTestIds.makeMention(use.sceneId)}
            onClick={() => onMakeMention(use)}
            className="border-0 bg-transparent p-0 text-xs font-medium text-accent hover:underline"
          >
            Make mention
          </button>
        )}
        {canWrite && kinds.includes('linked') && (
          <button
            type="button"
            data-testid={kbTestIds.unlinkScene(use.sceneId)}
            onClick={() => onUnlink(use)}
            className="border-0 bg-transparent p-0 text-xs font-medium text-accent hover:underline"
          >
            Unlink
          </button>
        )}
        <Link
          to={writeLink(use.bookId, { scene: use.number })}
          data-testid={kbTestIds.openInWrite(use.sceneId)}
          className="text-xs font-medium text-accent no-underline hover:underline"
        >
          Open in Write
        </Link>
      </div>
      {use.snippet !== '' && (
        <p data-testid={kbTestIds.useSnippet(use.sceneId)} className="m-0 mt-1 text-xs text-muted">
          {use.snippet}
        </p>
      )}
    </li>
  );
}
