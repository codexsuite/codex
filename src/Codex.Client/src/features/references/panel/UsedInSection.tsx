import type { SourceDetail, UseKind } from '@codex/core/api/referenceSources';
import { lookOf } from '../sourceStatus';
import { refsTestIds } from '../testIds';
import { UseItem } from './UseItem';

// Each passage that cites the source, in reading order.
export function UsedInSection({
  source,
  busy,
  onKind,
}: {
  source: SourceDetail;
  busy: boolean;
  onKind: (useId: string, kind: UseKind) => void;
}) {
  if (source.uses.length === 0) {
    return (
      <p data-testid={refsTestIds.usesEmpty} className="m-0 text-sm text-muted">
        No markers yet. In Write, type / and pick Source reference.
      </p>
    );
  }
  const marker = {
    number: source.number,
    tone: lookOf(source.verdict.status)?.tone ?? 'var(--color-muted)',
  };
  return (
    <div data-testid={refsTestIds.uses} className="flex flex-col">
      {source.uses.map((use) => (
        <UseItem
          key={use.id}
          bookId={source.bookId}
          sourceId={source.id}
          use={use}
          marker={marker}
          busy={busy}
          onKind={(kind) => onKind(use.id, kind)}
        />
      ))}
    </div>
  );
}
